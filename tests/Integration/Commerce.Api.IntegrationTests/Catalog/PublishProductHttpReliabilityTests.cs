using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Commerce.Api.IntegrationTests.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using CatalogAuthorization = global::Catalog.Api.Authorization.CatalogAuthorization;

namespace Commerce.Api.IntegrationTests.Catalog;

public sealed class PublishProductHttpReliabilityTests :
    IClassFixture<CommerceApiFixture>
{
    private const string FailureConstraint =
        "ck_test_http_publication_outbox_failure";

    private const string PublishedMessageType =
        "catalog.product-published.v1";

    private const string InvalidationMessageType =
        "catalog.storefront-product-cache-invalidate.v1";

    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] ForbiddenProblemFragments =
    [
        "DbUpdateConcurrencyException", "CatalogOptimisticConcurrencyException",
        "DbUpdateException", "PostgresException", "Npgsql", "Microsoft.EntityFrameworkCore",
        "catalog.outbox_messages", "catalog.products", FailureConstraint,
        "SqlState", "ConstraintName", "stackTrace", "innerException", "ConnectionStrings", "Host="
    ];

    private static readonly string[] AllowedProblemFields =
        ["type", "title", "status", "detail", "instance", "traceId", "code"];

    private readonly CommerceApiFixture _fixture;

    public PublishProductHttpReliabilityTests(CommerceApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentHttpPublicationsReturnOneSuccessAndOneSafeConflict()
    {
        var product = await SeedAsync("ecp11g3-http-race-");
        AssertDraft(await ReadStateAsync(product.Id.Value));

        var probe = new PublicationSaveProbe(product.Id.Value, participants: 2);
        await using var factory = CreateObservedFactory(probe);
        using var client = CreateAuthorizedClient(factory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        var path = GetPublishPath(product.Id.Value);
        // The interceptor releases both real saves only after both request scopes arrive.
        var first = client.PostAsync(path, null, timeout.Token);
        var second = client.PostAsync(path, null, timeout.Token);

        try
        {
            var responses = await Task.WhenAll(first, second);
            var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

            var publishedAtUtc = await ReadSuccessAsync(success, product.Id.Value);
            await AssertSafeProblemAsync(
                conflict, HttpStatusCode.Conflict, path,
                "Catalog.Product.ConcurrencyConflict");
            AssertNoStore(conflict);

            Assert.Equal(2, probe.SaveCalls);
            Assert.Equal(2, probe.Attempts.Length);
            Assert.All(probe.Attempts, AssertPreparedPublication);
            Assert.NotEqual(probe.Attempts[0].ContextId, probe.Attempts[1].ContextId);
            var successfulContext = Assert.Single(probe.SuccessfulContexts);
            var conflictObservation = Assert.Single(probe.ConcurrencyFailures);
            Assert.NotEqual(successfulContext, conflictObservation.ContextId);
            Assert.IsType<DbUpdateConcurrencyException>(conflictObservation.Exception);
            Assert.Empty(probe.TechnicalFailures);

            var winner = Assert.Single(
                probe.Attempts, attempt => attempt.ContextId == successfulContext);
            var loser = Assert.Single(
                probe.Attempts, attempt => attempt.ContextId == conflictObservation.ContextId);
            Assert.Equal(winner.PublishedAtUtc, publishedAtUtc);

            var persisted = await ReadStateAsync(product.Id.Value);
            AssertPublication(persisted, product, publishedAtUtc);
            foreach (var message in persisted.Messages)
            {
                Assert.Contains(message.Id, winner.IntentIds);
                Assert.DoesNotContain(message.Id, loser.IntentIds);
            }

            // A later request has a different semantic outcome: AlreadyPublished, not a new race.
            using var freshClient = _fixture.CreateClient(authenticated: true, authorized: true);
            using var repeated = await freshClient.PostAsync(
                path, null, TestContext.Current.CancellationToken);
            await AssertSafeProblemAsync(
                repeated, HttpStatusCode.Conflict, path, "Catalog.Product.AlreadyPublished");
            AssertSameState(persisted, await ReadStateAsync(product.Id.Value));
        }
        finally
        {
            await timeout.CancelAsync();
            // WhenAll completes only after both requests complete, including failures.
            if (first.IsCompletedSuccessfully)
            {
                using var response = await first;
            }
            if (second.IsCompletedSuccessfully)
            {
                using var response = await second;
            }
        }
    }

    [Fact]
    public async Task OutboxDatabaseFailureReturnsSafe500RollsBackAndAllowsFreshHttpRetry()
    {
        var product = await SeedAsync("ecp11g3-http-fault-");
        var before = await ReadStateAsync(product.Id.Value);
        AssertDraft(before);
        var probe = new PublicationSaveProbe(product.Id.Value, participants: 1);
        var path = GetPublishPath(product.Id.Value);

        try
        {
            await InstallFailureConstraintAsync();
            await using var factory = CreateObservedFactory(probe);
            using var client = CreateAuthorizedClient(factory);
            using var response = await client.PostAsync(
                path, null, TestContext.Current.CancellationToken);

            await AssertSafeProblemAsync(response, HttpStatusCode.InternalServerError, path);
            Assert.Equal(1, probe.SaveCalls);
            AssertPreparedPublication(Assert.Single(probe.Attempts));
            Assert.Empty(probe.SuccessfulContexts);
            Assert.Empty(probe.ConcurrencyFailures);

            var failure = Assert.Single(probe.TechnicalFailures);
            var updateException = Assert.IsType<DbUpdateException>(failure.Exception);
            var postgresException = Assert.IsType<PostgresException>(updateException.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
            Assert.Equal(FailureConstraint, postgresException.ConstraintName);

            AssertSameState(before, await ReadStateAsync(product.Id.Value));
        }
        finally
        {
            // Independent cleanup token: a cancelled test must still release its test-only constraint.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await RemoveFailureConstraintAsync(cleanup.Token);
        }

        using var freshClient = _fixture.CreateClient(authenticated: true, authorized: true);
        using var retry = await freshClient.PostAsync(
            path, null, TestContext.Current.CancellationToken);
        var publishedAtUtc = await ReadSuccessAsync(retry, product.Id.Value);
        var persisted = await ReadStateAsync(product.Id.Value);
        AssertPublication(persisted, product, publishedAtUtc);

        var failedAttempt = Assert.Single(probe.Attempts);
        foreach (var message in persisted.Messages)
        {
            Assert.DoesNotContain(message.Id, failedAttempt.IntentIds);
        }
    }

    private WebApplicationFactory<Program> CreateObservedFactory(PublicationSaveProbe probe)
    {
        return _fixture.CreateFactory(services =>
            services.ConfigureDbContext<CatalogDbContext>(options =>
                options.AddInterceptors(probe)));
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.PermissionHeader,
            CatalogAuthorization.ProductsWritePermission);
        return client;
    }

    private static async Task<DateTimeOffset> ReadSuccessAsync(
        HttpResponseMessage response, Guid productId)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertNoStore(response);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = document.RootElement;
        Assert.Equal(3, body.EnumerateObject().Count());
        Assert.Equal(productId, body.GetProperty("productId").GetGuid());
        Assert.Equal("published", body.GetProperty("status").GetString());
        var timestamp = body.GetProperty("publishedAtUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);
        return timestamp;
    }

    private static async Task AssertSafeProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string path, string? code = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        var problem = document.RootElement;
        foreach (var property in problem.EnumerateObject())
        {
            Assert.Contains(property.Name, AllowedProblemFields);
        }
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(path, problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));

        if (code is not null)
        {
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.Equal("urn:commerce:error:" + code, problem.GetProperty("type").GetString());
        }
        else
        {
            Assert.False(problem.TryGetProperty("code", out _));
            Assert.False(problem.TryGetProperty("detail", out var detail) &&
                detail.ValueKind != JsonValueKind.Null &&
                !string.IsNullOrEmpty(detail.GetString()));
        }

        // Inspect decoded JSON values too, so Unicode escaping cannot hide a forbidden fragment.
        AssertNoTechnicalDetails(problem);
        var policy = response.Headers.CacheControl;
        Assert.NotNull(policy);
        Assert.True(policy.NoStore);
    }

    private static void AssertNoTechnicalDetails(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                AssertSafeText(property.Name);
                AssertNoTechnicalDetails(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertNoTechnicalDetails(item);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            AssertSafeText(element.GetString()!);
        }
    }

    private static void AssertSafeText(string text)
    {
        foreach (var fragment in ForbiddenProblemFragments)
        {
            Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task InstallFailureConstraintAsync()
    {
        var dataSource = _fixture.Services.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand("""
            SET lock_timeout = '5s';
            SET statement_timeout = '10s';
            ALTER TABLE catalog.outbox_messages
            ADD CONSTRAINT ck_test_http_publication_outbox_failure
            CHECK (NOT (
                type = 'catalog.product-published.v1'
                AND (payload ->> 'slug') LIKE 'ecp11g3-http-fault-%'
            ));
            """);
        command.CommandTimeout = 15;
        _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task RemoveFailureConstraintAsync(CancellationToken cancellationToken)
    {
        var dataSource = _fixture.Services.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand("""
            SET lock_timeout = '5s';
            SET statement_timeout = '10s';
            ALTER TABLE catalog.outbox_messages
            DROP CONSTRAINT IF EXISTS ck_test_http_publication_outbox_failure;
            """);
        command.CommandTimeout = 15;
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AssertPreparedPublication(SaveObservation attempt)
    {
        Assert.Equal(1L, attempt.OriginalVersion);
        Assert.Equal(2L, attempt.PendingVersion);
        Assert.Equal(ProductStatus.Draft, attempt.OriginalStatus);
        Assert.Equal(ProductStatus.Published, attempt.PendingStatus);
        Assert.Equal(TimeSpan.Zero, attempt.PublishedAtUtc.Offset);
        Assert.Equal(2, attempt.IntentIds.Length);
        Assert.NotEqual(attempt.IntentIds[0], attempt.IntentIds[1]);
    }

    // Test-only observation and scheduling. Never replaces a save result or suppresses an exception.
    private sealed class PublicationSaveProbe(Guid productId, int participants) : SaveChangesInterceptor
    {
        private readonly ConcurrentDictionary<Guid, SaveObservation> _attempts = new();
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _saveCalls;

        public SaveObservation[] Attempts => _attempts.Values.ToArray();
        public int SaveCalls => Volatile.Read(ref _saveCalls);
        public ConcurrentQueue<Guid> SuccessfulContexts { get; } = new();
        public ConcurrentQueue<FailureObservation> ConcurrencyFailures { get; } = new();
        public ConcurrentQueue<FailureObservation> TechnicalFailures { get; } = new();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context ?? throw new InvalidOperationException("A save requires a context.");
            var entries = context.ChangeTracker.Entries().ToArray();
            var product = entries.SingleOrDefault(entry =>
                entry.Metadata.GetSchema() == "catalog" &&
                entry.Metadata.GetTableName() == "products" &&
                Equals(entry.Property("Id").CurrentValue, productId));
            if (product is null)
            {
                return result;
            }

            var intentIds = entries.Where(entry =>
                    entry.Metadata.GetSchema() == "catalog" &&
                    entry.Metadata.GetTableName() == "outbox_messages" &&
                    entry.State == EntityState.Added)
                .Select(entry => (Guid)entry.Property("Id").CurrentValue!)
                .ToArray();
            var attempt = new SaveObservation(
                context.ContextId.InstanceId,
                (long)product.Property("Version").OriginalValue!,
                (long)product.Property("Version").CurrentValue!,
                (ProductStatus)product.Property("Status").OriginalValue!,
                (ProductStatus)product.Property("Status").CurrentValue!,
                (DateTimeOffset)product.Property("PublishedAtUtc").CurrentValue!,
                intentIds);
            Interlocked.Increment(ref _saveCalls);
            if (!_attempts.TryAdd(attempt.ContextId, attempt))
            {
                throw new InvalidOperationException("A publication request attempted to save more than once.");
            }
            if (_attempts.Count == participants)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && _attempts.ContainsKey(context.ContextId.InstanceId))
            {
                SuccessfulContexts.Enqueue(context.ContextId.InstanceId);
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && _attempts.ContainsKey(context.ContextId.InstanceId))
            {
                ConcurrencyFailures.Enqueue(new FailureObservation(context.ContextId.InstanceId, eventData.Exception));
            }
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context &&
                _attempts.ContainsKey(context.ContextId.InstanceId) &&
                eventData.Exception is not DbUpdateConcurrencyException)
            {
                TechnicalFailures.Enqueue(new FailureObservation(context.ContextId.InstanceId, eventData.Exception));
            }
            return Task.CompletedTask;
        }
    }

    private sealed record SaveObservation(
        Guid ContextId, long OriginalVersion, long PendingVersion,
        ProductStatus OriginalStatus, ProductStatus PendingStatus,
        DateTimeOffset PublishedAtUtc, Guid[] IntentIds);

    private sealed record FailureObservation(Guid ContextId, Exception Exception);

    private static void AssertDraft(PersistenceState state)
    {
        Assert.Equal("Draft", state.Product.Status);
        Assert.Equal(1L, state.Product.Version);
        Assert.Null(state.Product.PublishedAtUtc);
        Assert.Null(state.Product.DiscontinuedAtUtc);
        Assert.Equal(2, state.Variants.Length);
        Assert.All(state.Variants, variant =>
        {
            Assert.Equal("Draft", variant.Status);
            Assert.Null(variant.ActivatedAtUtc);
            Assert.Null(variant.DiscontinuedAtUtc);
        });
        Assert.Empty(state.Messages);
    }

    private static void AssertPublication(
        PersistenceState state, Product product, DateTimeOffset publishedAtUtc)
    {
        Assert.Equal("Published", state.Product.Status);
        Assert.Equal(2L, state.Product.Version);
        Assert.Equal(ToPostgreSqlPrecision(publishedAtUtc), state.Product.PublishedAtUtc);
        Assert.Null(state.Product.DiscontinuedAtUtc);
        Assert.Equal(2, state.Variants.Length);
        Assert.All(state.Variants, variant =>
        {
            Assert.Equal("Active", variant.Status);
            Assert.Equal(ToPostgreSqlPrecision(publishedAtUtc), variant.ActivatedAtUtc);
            Assert.Null(variant.DiscontinuedAtUtc);
        });
        Assert.Equal(2, state.Messages.Length);
        Assert.NotEqual(state.Messages[0].Id, state.Messages[1].Id);
        Assert.Equal(PublishedMessageType, state.Messages[0].MessageType);
        Assert.Equal(InvalidationMessageType, state.Messages[1].MessageType);
        Assert.All(state.Messages, message =>
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

    private async Task<Product> SeedAsync(string slugPrefix)
    {
        var suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        var product = Product.CreateDraft(
            ProductName.Create("HTTP publication product").Value,
            ProductSlug.Create(slugPrefix + suffix).Value,
            ProductDescription.Empty,
            SeedTime);

        var option = product.DefineOption(OptionName.Create("Color").Value, 0);
        Assert.True(option.IsSuccess, option.Error?.Code);
        AddVariant(product, option.Value, "Black", "HTTP-A-" + suffix);
        AddVariant(product, option.Value, "White", "HTTP-B-" + suffix);

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
