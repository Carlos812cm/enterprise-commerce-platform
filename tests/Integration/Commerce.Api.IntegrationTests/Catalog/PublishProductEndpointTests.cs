using System.Globalization;
using System.Net;
using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Commerce.Api.IntegrationTests.Catalog;

public sealed class PublishProductEndpointTests :
    IClassFixture<CommerceApiFixture>
{
    private const string OperationPath =
        "/api/catalog/products/{productId}/publish";

    private const string PublishedMessageType =
        "catalog.product-published.v1";

    private const string InvalidationMessageType =
        "catalog.storefront-product-cache-invalidate.v1";

    private static readonly string[] ExpectedResponseCodes =
        ["200", "400", "401", "403", "404", "409", "500"];

    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CommerceApiFixture _fixture;

    public PublishProductEndpointTests(CommerceApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndpointPublishesWithoutBodyAndPersistsTwoOutboxIntents()
    {
        var product = await SeedAsync(withVariants: true);
        var before = await ReadStateAsync(product.Id.Value);
        Assert.Equal("Draft", before.Product.Status);
        Assert.Equal(1L, before.Product.Version);
        Assert.Empty(before.Messages);

        using var client = _fixture.CreateClient(
            authenticated: true, authorized: true);
        using var response = await client.PostAsync(
            GetPublishPath(product.Id.Value), null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertNoStore(response);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = document.RootElement;
        Assert.Equal(3, body.EnumerateObject().Count());
        Assert.Equal(product.Id.Value, body.GetProperty("productId").GetGuid());
        Assert.Equal("published", body.GetProperty("status").GetString());
        var publishedAtUtc = body.GetProperty("publishedAtUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, publishedAtUtc.Offset);

        var after = await ReadStateAsync(product.Id.Value);
        Assert.Equal("Published", after.Product.Status);
        Assert.Equal(2L, after.Product.Version);
        Assert.Equal(ToPostgreSqlPrecision(publishedAtUtc), after.Product.PublishedAtUtc);
        Assert.Null(after.Product.DiscontinuedAtUtc);
        Assert.Equal(2, after.Variants.Length);
        Assert.All(after.Variants, variant =>
        {
            Assert.Equal("Active", variant.Status);
            Assert.Equal(ToPostgreSqlPrecision(publishedAtUtc), variant.ActivatedAtUtc);
            Assert.Null(variant.DiscontinuedAtUtc);
        });

        Assert.Equal(2, after.Messages.Length);
        Assert.NotEqual(after.Messages[0].Id, after.Messages[1].Id);
        Assert.Equal(PublishedMessageType, after.Messages[0].MessageType);
        Assert.Equal(InvalidationMessageType, after.Messages[1].MessageType);
        Assert.All(after.Messages, message =>
        {
            Assert.NotEqual(Guid.Empty, message.Id);
            Assert.Equal(0, message.AttemptCount);
            Assert.True(message.IsPending);
            Assert.True(message.IsUnleased);
            Assert.Equal(ToPostgreSqlPrecision(publishedAtUtc), message.OccurredAtUtc);
            using var payload = JsonDocument.Parse(message.Payload);
            Assert.Equal(product.Id.Value, payload.RootElement.GetProperty("productId").GetGuid());
            Assert.Equal(product.Slug.Value, payload.RootElement.GetProperty("slug").GetString());
            Assert.Equal(publishedAtUtc, payload.RootElement.GetProperty("publishedAtUtc").GetDateTimeOffset());
        });
    }

    [Fact]
    public async Task AnonymousCallerCannotPublishOrCreateOutboxIntents()
    {
        var product = await SeedAsync(withVariants: true);
        var before = await ReadStateAsync(product.Id.Value);
        using var client = _fixture.CreateClient(
            authenticated: false, authorized: false);
        using var response = await client.PostAsync(
            GetPublishPath(product.Id.Value), null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertSameState(before, await ReadStateAsync(product.Id.Value));
    }

    [Fact]
    public async Task CallerWithoutPermissionCannotPublishOrCreateOutboxIntents()
    {
        var product = await SeedAsync(withVariants: true);
        var before = await ReadStateAsync(product.Id.Value);
        using var client = _fixture.CreateClient(
            authenticated: true, authorized: false);
        using var response = await client.PostAsync(
            GetPublishPath(product.Id.Value), null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertSameState(before, await ReadStateAsync(product.Id.Value));
    }

    [Fact]
    public async Task EmptyIdReturnsValidationProblemWithTraceId()
    {
        using var client = _fixture.CreateClient(
            authenticated: true, authorized: true);
        var path = GetPublishPath(Guid.Empty);
        using var response = await client.PostAsync(
            path, null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.BadRequest,
            "Catalog.Product.InvalidId", path);
    }

    [Fact]
    public async Task MissingProductReturnsNotFoundProblemWithTraceId()
    {
        using var client = _fixture.CreateClient(
            authenticated: true, authorized: true);
        var path = GetPublishPath(Guid.CreateVersion7());
        using var response = await client.PostAsync(
            path, null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.NotFound,
            "Catalog.Product.NotFound", path);
    }

    [Fact]
    public async Task DraftWithoutVariantsReturnsValidationAndLeavesDatabaseUnchanged()
    {
        var product = await SeedAsync(withVariants: false);
        var before = await ReadStateAsync(product.Id.Value);
        using var client = _fixture.CreateClient(
            authenticated: true, authorized: true);
        var path = GetPublishPath(product.Id.Value);
        using var response = await client.PostAsync(
            path, null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.BadRequest,
            "Catalog.Product.NoPublishableVariants", path);
        AssertSameState(before, await ReadStateAsync(product.Id.Value));
    }

    [Fact]
    public async Task RepeatedHttpPublicationReturnsConflictWithoutDuplicateIntents()
    {
        var product = await SeedAsync(withVariants: true);
        var path = GetPublishPath(product.Id.Value);
        using (var firstClient = _fixture.CreateClient(
            authenticated: true, authorized: true))
        {
            using var firstResponse = await firstClient.PostAsync(
                path, null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        var committed = await ReadStateAsync(product.Id.Value);
        Assert.Equal(2L, committed.Product.Version);
        Assert.Equal(2, committed.Messages.Length);

        using var secondClient = _fixture.CreateClient(
            authenticated: true, authorized: true);
        using var secondResponse = await secondClient.PostAsync(
            path, null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            secondResponse, HttpStatusCode.Conflict,
            "Catalog.Product.AlreadyPublished", path);
        AssertSameState(committed, await ReadStateAsync(product.Id.Value));
    }

    [Fact]
    public async Task DiscontinuedProductReturnsConflictAndLeavesDatabaseUnchanged()
    {
        var product = await SeedAsync(withVariants: true, discontinued: true);
        var before = await ReadStateAsync(product.Id.Value);
        using var client = _fixture.CreateClient(
            authenticated: true, authorized: true);
        var path = GetPublishPath(product.Id.Value);
        using var response = await client.PostAsync(
            path, null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.Conflict,
            "Catalog.Product.IsDiscontinued", path);
        AssertSameState(before, await ReadStateAsync(product.Id.Value));
    }

    [Fact]
    public async Task MalformedGuidDoesNotMatchPublicationRoute()
    {
        using var client = _fixture.CreateClient(
            authenticated: true, authorized: true);
        using var response = await client.PostAsync(
            "/api/catalog/products/not-a-guid/publish", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentsPublicationResponsesWithoutRequestBody()
    {
        using var client = _fixture.CreateClient(
            authenticated: false, authorized: false);
        using var response = await client.GetAsync(
            "/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty(OperationPath).GetProperty("post");
        Assert.Equal("PublishProduct", operation.GetProperty("operationId").GetString());
        Assert.False(operation.TryGetProperty("requestBody", out _));
        var responses = operation.GetProperty("responses");
        foreach (var code in ExpectedResponseCodes)
        {
            Assert.True(responses.TryGetProperty(code, out _), code);
        }

        Assert.True(responses.GetProperty("200").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(responses.GetProperty("409").GetProperty("content")
            .TryGetProperty("application/problem+json", out _));
    }

    private async Task<Product> SeedAsync(bool withVariants, bool discontinued = false)
    {
        var suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        var product = Product.CreateDraft(
            ProductName.Create("HTTP publication product").Value,
            ProductSlug.Create("http-publication-" + suffix).Value,
            ProductDescription.Empty,
            SeedTime);

        if (withVariants)
        {
            var option = product.DefineOption(OptionName.Create("Color").Value, 0);
            Assert.True(option.IsSuccess, option.Error?.Code);
            AddVariant(product, option.Value, "Black", "HTTP-A-" + suffix);
            AddVariant(product, option.Value, "White", "HTTP-B-" + suffix);
        }

        if (discontinued)
        {
            var result = product.Discontinue(SeedTime.AddMinutes(2));
            Assert.True(result.IsSuccess, result.Error?.Code);
        }

        await using var scope = _fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>();
        repository.Add(product);
        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        return product;
    }

    private static void AddVariant(
        Product product, ProductOptionId optionId, string value, string sku)
    {
        var combination = VariantOptionCombination.Create(
            [OptionSelection.Create(optionId, OptionValue.Create(value).Value)]);
        Assert.True(combination.IsSuccess, combination.Error?.Code);
        var result = product.AddVariant(
            Sku.Create(sku).Value, combination.Value, SeedTime.AddMinutes(1));
        Assert.True(result.IsSuccess, result.Error?.Code);
    }

    private static string GetPublishPath(Guid productId)
    {
        return "/api/catalog/products/" +
            productId.ToString("D", CultureInfo.InvariantCulture) + "/publish";
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value)
    {
        // PostgreSQL timestamps have microsecond precision; .NET ticks are 100 ns.
        return new DateTimeOffset(value.Ticks - (value.Ticks % 10), TimeSpan.Zero);
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        var policy = response.Headers.CacheControl;
        Assert.NotNull(policy);
        Assert.True(policy.Private);
        Assert.True(policy.NoStore);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string code, string path)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        AssertNoStore(response);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var problem = document.RootElement;
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(path, problem.GetProperty("instance").GetString());
        Assert.Equal("urn:commerce:error:" + code, problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    private async Task<PersistenceState> ReadStateAsync(Guid productId)
    {
        var dataSource = _fixture.Services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, version, published_at_utc, discontinued_at_utc
            FROM catalog.products WHERE id = @product_id;

            SELECT id, status, activated_at_utc, discontinued_at_utc
            FROM catalog.product_variants WHERE product_id = @product_id
            ORDER BY id;

            SELECT id, type, payload::text, occurred_at_utc, attempt_count,
                processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL,
                lock_owner IS NULL AND locked_until_utc IS NULL
            FROM catalog.outbox_messages
            WHERE (payload ->> 'productId')::uuid = @product_id
            ORDER BY type, id;
            """;
        command.Parameters.AddWithValue("product_id", productId);
        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        var root = new ProductState(
            reader.GetString(0), reader.GetInt64(1),
            ReadTimestamp(reader, 2), ReadTimestamp(reader, 3));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
        var variants = new List<VariantState>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            variants.Add(new VariantState(
                reader.GetGuid(0), reader.GetString(1),
                ReadTimestamp(reader, 2), ReadTimestamp(reader, 3)));
        }

        Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
        var messages = new List<OutboxState>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            messages.Add(new OutboxState(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetInt32(4),
                reader.GetBoolean(5), reader.GetBoolean(6)));
        }

        Assert.False(await reader.NextResultAsync(TestContext.Current.CancellationToken));
        return new PersistenceState(root, variants.ToArray(), messages.ToArray());
    }

    private static DateTimeOffset? ReadTimestamp(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static void AssertSameState(PersistenceState expected, PersistenceState actual)
    {
        Assert.Equal(expected.Product, actual.Product);
        Assert.Equal<VariantState>(expected.Variants, actual.Variants);
        Assert.Equal<OutboxState>(expected.Messages, actual.Messages);
    }

    private sealed record PersistenceState(
        ProductState Product, VariantState[] Variants, OutboxState[] Messages);

    private sealed record ProductState(
        string Status, long Version,
        DateTimeOffset? PublishedAtUtc, DateTimeOffset? DiscontinuedAtUtc);

    private sealed record VariantState(
        Guid Id, string Status,
        DateTimeOffset? ActivatedAtUtc, DateTimeOffset? DiscontinuedAtUtc);

    private sealed record OutboxState(
        Guid Id, string MessageType, string Payload, DateTimeOffset OccurredAtUtc,
        int AttemptCount, bool IsPending, bool IsUnleased);
}
