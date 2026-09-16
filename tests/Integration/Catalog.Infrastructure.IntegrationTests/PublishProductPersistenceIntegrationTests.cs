using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.PublishProduct;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class PublishProductPersistenceIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private const string FailureConstraint =
        "ck_ecp11g3_test_reject_publication";

    private static readonly DateTimeOffset PublicationTime =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly CatalogPostgreSqlFixture _fixture;

    public PublishProductPersistenceIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DispatcherCommitsProductVariantsAndTwoOutboxIntents()
    {
        var clock = new FakeTimeProvider(PublicationTime);
        await using var provider = _fixture.CreateServiceProvider(clock);
        var product = await SeedDraftAsync(provider, "ecp11g3-commit", true);

        var before = await ReadStateAsync(provider, product.Id.Value);
        AssertDraft(before, 2);

        var result = await DispatchInNewScopeAsync(provider, product.Id.Value);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(product.Id, result.Value.ProductId);
        Assert.Equal(ProductStatus.Published, result.Value.Status);
        Assert.Equal(PublicationTime, result.Value.PublishedAtUtc);

        var committed = await ReadStateAsync(provider, product.Id.Value);
        AssertPublication(committed, product, result.Value.PublishedAtUtc);
    }

    [Fact]
    public async Task RepeatedPublicationFromNewScopeDoesNotAppendOutboxIntents()
    {
        var clock = new FakeTimeProvider(PublicationTime);
        await using var provider = _fixture.CreateServiceProvider(clock);
        var product = await SeedDraftAsync(provider, "ecp11g3-repeat", true);

        var first = await DispatchInNewScopeAsync(provider, product.Id.Value);
        Assert.True(first.IsSuccess, first.Error?.Code);

        var beforeRepeat = await ReadStateAsync(provider, product.Id.Value);
        AssertPublication(beforeRepeat, product, PublicationTime);

        clock.Advance(TimeSpan.FromMinutes(1));
        var repeated = await DispatchInNewScopeAsync(provider, product.Id.Value);

        Assert.True(repeated.IsFailure);
        Assert.Equal("Catalog.Product.AlreadyPublished", repeated.Error?.Code);
        Assert.Equal(ErrorType.Conflict, repeated.Error!.Type);

        var afterRepeat = await ReadStateAsync(provider, product.Id.Value);
        AssertUnchanged(beforeRepeat, afterRepeat);
    }

    [Fact]
    public async Task UnpublishableDraftLeavesDatabaseUnchanged()
    {
        await using var provider = _fixture.CreateServiceProvider(
            new FakeTimeProvider(PublicationTime));
        var product = await SeedDraftAsync(provider, "ecp11g3-empty", false);
        var before = await ReadStateAsync(provider, product.Id.Value);
        AssertDraft(before, 0);

        var result = await DispatchInNewScopeAsync(provider, product.Id.Value);

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.Product.NoPublishableVariants", result.Error?.Code);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);

        var after = await ReadStateAsync(provider, product.Id.Value);
        AssertUnchanged(before, after);
    }

    [Fact]
    public async Task OutboxConstraintFailureRollsBackAndFreshScopeCanRetry()
    {
        var clock = new FakeTimeProvider(PublicationTime);
        await using var provider = _fixture.CreateServiceProvider(clock);
        var product = await SeedDraftAsync(provider, "ecp11g3-rollback", true);
        var before = await ReadStateAsync(provider, product.Id.Value);
        AssertDraft(before, 2);

        try
        {
            await InstallFailureConstraintAsync(provider);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => DispatchInNewScopeAsync(provider, product.Id.Value));

            var postgresException = Assert.IsType<PostgresException>(
                exception.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
            Assert.Equal(FailureConstraint, postgresException.ConstraintName);

            // Read through Npgsql after the failed command scope is disposed.
            // The assertion observes database state, not EF's tracked entities.
            var rolledBack = await ReadStateAsync(provider, product.Id.Value);
            AssertUnchanged(before, rolledBack);
        }
        finally
        {
            // Cleanup has its own bounded token even when the test is cancelled.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await RemoveFailureConstraintAsync(provider, cleanup.Token);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        var retried = await DispatchInNewScopeAsync(provider, product.Id.Value);

        Assert.True(retried.IsSuccess, retried.Error?.Code);
        Assert.Equal(clock.GetUtcNow(), retried.Value.PublishedAtUtc);

        var committed = await ReadStateAsync(provider, product.Id.Value);
        AssertPublication(committed, product, retried.Value.PublishedAtUtc);
    }

    private static async Task<Product> SeedDraftAsync(
        ServiceProvider provider,
        string slugPrefix,
        bool withVariants)
    {
        var product = Product.CreateDraft(
            ProductName.Create("Publication Persistence Product").Value,
            ProductSlug.Create($"{slugPrefix}-{Guid.CreateVersion7():N}").Value,
            ProductDescription.Empty,
            PublicationTime.AddMinutes(-2));

        if (withVariants)
        {
            var option = product.DefineOption(
                OptionName.Create("Color").Value,
                displayOrder: 0);
            Assert.True(option.IsSuccess, option.Error?.Code);

            AddDraftVariant(product, option.Value, "Black");
            AddDraftVariant(product, option.Value, "White");
        }

        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>();

        repository.Add(product);
        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(product.DomainEvents);
        return product;
    }

    private static void AddDraftVariant(
        Product product,
        ProductOptionId optionId,
        string optionValue)
    {
        var selection = OptionSelection.Create(
            optionId,
            OptionValue.Create(optionValue).Value);
        var combination = VariantOptionCombination.Create([selection]);
        Assert.True(combination.IsSuccess, combination.Error?.Code);

        var added = product.AddVariant(
            Sku.Create($"PUB-{Guid.CreateVersion7():N}").Value,
            combination.Value,
            PublicationTime.AddMinutes(-1));
        Assert.True(added.IsSuccess, added.Error?.Code);
    }

    private static async Task<Result<PublishProductResponse>> DispatchInNewScopeAsync(
        ServiceProvider provider,
        Guid productId)
    {
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        return await dispatcher.DispatchAsync(
            new PublishProductCommand(productId),
            TestContext.Current.CancellationToken);
    }

    private static async Task<PersistenceState> ReadStateAsync(
        ServiceProvider provider,
        Guid productId)
    {
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        var product = await ReadProductAsync(dataSource, productId);
        var variants = await ReadVariantsAsync(dataSource, productId);
        var messages = await ReadMessagesAsync(dataSource, productId);
        return new PersistenceState(product, variants, messages);
    }

    private static async Task<ProductState> ReadProductAsync(
        NpgsqlDataSource dataSource,
        Guid productId)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT status, version, published_at_utc, discontinued_at_utc
            FROM catalog.products
            WHERE id = @product_id;
            """);
        command.Parameters.AddWithValue("product_id", productId);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        var product = new ProductState(
            reader.GetString(0),
            reader.GetInt64(1),
            ReadNullableTimestamp(reader, 2),
            ReadNullableTimestamp(reader, 3));

        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return product;
    }

    private static async Task<VariantState[]> ReadVariantsAsync(
        NpgsqlDataSource dataSource,
        Guid productId)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, sku, status, activated_at_utc, discontinued_at_utc
            FROM catalog.product_variants
            WHERE product_id = @product_id
            ORDER BY id;
            """);
        command.Parameters.AddWithValue("product_id", productId);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var variants = new List<VariantState>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            variants.Add(new VariantState(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadNullableTimestamp(reader, 3),
                ReadNullableTimestamp(reader, 4)));
        }

        return variants.ToArray();
    }

    private static async Task<OutboxState[]> ReadMessagesAsync(
        NpgsqlDataSource dataSource,
        Guid productId)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, type, payload::text, occurred_at_utc, attempt_count,
                   processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL,
                   lock_owner IS NULL AND locked_until_utc IS NULL
            FROM catalog.outbox_messages
            WHERE (payload ->> 'productId')::uuid = @product_id
            ORDER BY type, id;
            """);
        command.Parameters.AddWithValue("product_id", productId);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var messages = new List<OutboxState>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            messages.Add(new OutboxState(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetInt32(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6)));
        }

        return messages.ToArray();
    }

    private static DateTimeOffset? ReadNullableTimestamp(
        NpgsqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static void AssertDraft(PersistenceState state, int variantCount)
    {
        Assert.Equal("Draft", state.Product.Status);
        Assert.Equal(1L, state.Product.Version);
        Assert.Null(state.Product.PublishedAtUtc);
        Assert.Null(state.Product.DiscontinuedAtUtc);
        Assert.Equal(variantCount, state.Variants.Length);
        Assert.All(state.Variants, variant =>
        {
            Assert.Equal("Draft", variant.Status);
            Assert.Null(variant.ActivatedAtUtc);
            Assert.Null(variant.DiscontinuedAtUtc);
        });
        Assert.Empty(state.Messages);
    }

    private static void AssertPublication(
        PersistenceState state,
        Product seededProduct,
        DateTimeOffset expectedTime)
    {
        Assert.Equal("Published", state.Product.Status);
        Assert.Equal(2L, state.Product.Version);
        Assert.Equal(expectedTime, state.Product.PublishedAtUtc);
        Assert.Null(state.Product.DiscontinuedAtUtc);
        Assert.Equal(2, state.Variants.Length);
        Assert.All(state.Variants, variant =>
        {
            Assert.Equal("Active", variant.Status);
            Assert.Equal(expectedTime, variant.ActivatedAtUtc);
            Assert.Null(variant.DiscontinuedAtUtc);
            var original = Assert.Single(
                seededProduct.Variants,
                candidate => candidate.Id.Value == variant.Id);
            Assert.Equal(original.Sku.Value, variant.Sku);
        });
        Assert.Collection(
            state.Messages,
            message => AssertMessage(
                message, "catalog.product-published.v1", seededProduct, expectedTime),
            message => AssertMessage(
                message, "catalog.storefront-product-cache-invalidate.v1", seededProduct, expectedTime));
        Assert.NotEqual(state.Messages[0].Id, state.Messages[1].Id);
    }

    private static void AssertMessage(
        OutboxState message,
        string expectedType,
        Product seededProduct,
        DateTimeOffset expectedTime)
    {
        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(expectedType, message.MessageType);
        Assert.Equal(expectedTime, message.OccurredAtUtc);
        Assert.Equal(0, message.AttemptCount);
        Assert.True(message.IsPending);
        Assert.True(message.IsUnleased);

        using var document = JsonDocument.Parse(message.Payload);
        var payload = document.RootElement;
        Assert.Equal(seededProduct.Id.Value, payload.GetProperty("productId").GetGuid());
        Assert.Equal(seededProduct.Slug.Value, payload.GetProperty("slug").GetString());
        Assert.Equal(expectedTime, payload.GetProperty("publishedAtUtc").GetDateTimeOffset());
    }

    private static void AssertUnchanged(PersistenceState before, PersistenceState after)
    {
        Assert.Equal(before.Product, after.Product);
        Assert.Equal<VariantState>(before.Variants, after.Variants);
        Assert.Equal<OutboxState>(before.Messages, after.Messages);
    }

    private static async Task InstallFailureConstraintAsync(ServiceProvider provider)
    {
        // This provider belongs to CatalogPostgreSqlFixture, never to Compose.
        // NOT VALID leaves older rows alone; new rollback-test intents are rejected.
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand(
            """
            ALTER TABLE catalog.outbox_messages
            ADD CONSTRAINT ck_ecp11g3_test_reject_publication
            CHECK (
                type <> 'catalog.product-published.v1'
                OR (payload ->> 'slug') NOT LIKE 'ecp11g3-rollback-%'
            ) NOT VALID;
            """);
        _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task RemoveFailureConstraintAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand(
            """
            ALTER TABLE catalog.outbox_messages
            DROP CONSTRAINT IF EXISTS ck_ecp11g3_test_reject_publication;
            """);
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record PersistenceState(
        ProductState Product,
        VariantState[] Variants,
        OutboxState[] Messages);

    private sealed record ProductState(
        string Status,
        long Version,
        DateTimeOffset? PublishedAtUtc,
        DateTimeOffset? DiscontinuedAtUtc);

    private sealed record VariantState(
        Guid Id,
        string Sku,
        string Status,
        DateTimeOffset? ActivatedAtUtc,
        DateTimeOffset? DiscontinuedAtUtc);

    private sealed record OutboxState(
        Guid Id,
        string MessageType,
        string Payload,
        DateTimeOffset OccurredAtUtc,
        int AttemptCount,
        bool IsPending,
        bool IsUnleased);
}
