using System.Text.Json;
using Catalog.Application;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.PublishProduct;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Commerce.Application.Messaging;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class PublishProductConcurrencyIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly DateTimeOffset PublicationTime =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly CatalogPostgreSqlFixture _fixture;

    public PublishProductConcurrencyIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task OverlappingPublicationsRespectFirstCommittedVersion(int firstWriter)
    {
        await VerifyRaceAsync(firstWriter);
    }

    [Fact]
    public async Task SimultaneousSaveReleaseCommitsOnlyOnePublication()
    {
        await VerifyRaceAsync(null);
    }

    private async Task VerifyRaceAsync(int? firstWriter)
    {
        await using var owner = _fixture.CreateServiceProvider(
            new FakeTimeProvider(PublicationTime));
        var product = await SeedDraftAsync(owner);
        var before = await ReadStateAsync(owner, product.Id.Value);
        AssertDraft(before);

        // The owner supplies a data source for its disposable Testcontainers DB.
        // No Compose connection string or transport publisher is registered here.
        var winner = await RunCompetingCommandsAsync(owner, product, firstWriter);

        // Both command scopes have been disposed. Read persisted data via Npgsql.
        var committed = await ReadStateAsync(owner, product.Id.Value);
        AssertPublication(committed, product, winner.PublishedAtUtc);

        // A fresh request after the race must not create another publication.
        var repeated = await DispatchInNewScopeAsync(owner, product.Id.Value);
        Assert.True(repeated.IsFailure);
        Assert.Equal("Catalog.Product.AlreadyPublished", repeated.Error?.Code);
        Assert.Equal(ErrorType.Conflict, repeated.Error!.Type);

        var afterRepeat = await ReadStateAsync(owner, product.Id.Value);
        Assert.Equal(committed.Product, afterRepeat.Product);
        Assert.Equal<VariantState>(committed.Variants, afterRepeat.Variants);
        Assert.Equal<OutboxState>(committed.Messages, afterRepeat.Messages);
    }

    private static async Task<PublishProductResponse> RunCompetingCommandsAsync(
        ServiceProvider owner,
        Product product,
        int? firstWriter)
    {
        await using var provider = CreateCoordinatedProvider(
            owner.GetRequiredService<NpgsqlDataSource>());
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstServices = firstScope.ServiceProvider;
        var secondServices = secondScope.ServiceProvider;
        var firstAttempt = firstServices.GetRequiredService<PublicationAttempt>();
        var secondAttempt = secondServices.GetRequiredService<PublicationAttempt>();
        secondAttempt.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.NotSame(
            firstServices.GetRequiredService<CatalogDbContext>(),
            secondServices.GetRequiredService<CatalogDbContext>());
        Assert.NotSame(
            firstServices.GetRequiredService<IProductRepository>(),
            secondServices.GetRequiredService<IProductRepository>());
        Assert.NotSame(
            firstServices.GetRequiredService<ICatalogUnitOfWork>(),
            secondServices.GetRequiredService<ICatalogUnitOfWork>());

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));

        var firstTask = DispatchAsync(firstServices, product.Id.Value, deadline.Token);
        var secondTask = DispatchAsync(secondServices, product.Id.Value, deadline.Token);
        var completion = Task.WhenAll(firstTask, secondTask);
        PublicationAttempt[] attempts = [firstAttempt, secondAttempt];

        try
        {
            // Both real handlers have loaded and mutated their own aggregates.
            // Neither real Unit of Work is allowed to save until both are ready.
            await Task.WhenAll(firstAttempt.Ready, secondAttempt.Ready)
                .WaitAsync(deadline.Token);

            AssertAttemptReady(firstAttempt, product.Id);
            AssertAttemptReady(secondAttempt, product.Id);
            Assert.NotSame(firstAttempt.Product, secondAttempt.Product);
            AssertDraft(await ReadStateAsync(owner, product.Id.Value));

            if (firstWriter == 0)
            {
                firstAttempt.ReleaseSave();
                var firstResult = await firstTask.WaitAsync(deadline.Token);
                Assert.True(firstResult.IsSuccess, firstResult.Error?.Code);
                secondAttempt.ReleaseSave();
            }
            else if (firstWriter == 1)
            {
                secondAttempt.ReleaseSave();
                var secondResult = await secondTask.WaitAsync(deadline.Token);
                Assert.True(secondResult.IsSuccess, secondResult.Error?.Code);
                firstAttempt.ReleaseSave();
            }
            else
            {
                // Release both saves without waiting for either command to finish.
                firstAttempt.ReleaseSave();
                secondAttempt.ReleaseSave();
            }

            var results = await completion.WaitAsync(deadline.Token);
            var success = Assert.Single(results, result => result.IsSuccess);
            var failure = Assert.Single(results, result => result.IsFailure);
            var winnerIndex = results[0].IsSuccess ? 0 : 1;
            var loserIndex = 1 - winnerIndex;

            if (firstWriter is { } expectedWinner)
            {
                Assert.Equal(expectedWinner, winnerIndex);
            }

            Assert.Equal("Catalog.Product.ConcurrencyConflict", failure.Error?.Code);
            Assert.Equal(ErrorType.Conflict, failure.Error!.Type);
            Assert.Equal(product.Id, success.Value.ProductId);
            Assert.Equal(ProductStatus.Published, success.Value.Status);
            Assert.Equal(attempts[winnerIndex].Clock.GetUtcNow(), success.Value.PublishedAtUtc);
            Assert.NotEqual(attempts[loserIndex].Clock.GetUtcNow(), success.Value.PublishedAtUtc);
            Assert.All(attempts, attempt => Assert.Equal(1, attempt.SaveCalls));

            Assert.Null(attempts[winnerIndex].ConcurrencyFailure);
            var translated = Assert.IsType<CatalogOptimisticConcurrencyException>(
                attempts[loserIndex].ConcurrencyFailure);
            Assert.IsType<DbUpdateConcurrencyException>(translated.InnerException);

            // Successful commit clears only the winner's events. The failed save
            // leaves the loser's in-memory events intact, but persists none of them.
            Assert.Empty(attempts[winnerIndex].Product!.DomainEvents);
            Assert.Equal(3, attempts[loserIndex].Product!.DomainEvents.Count);
            return success.Value;
        }
        finally
        {
            // Cancel first, then unblock: cleanup must not start an unwanted save.
            await deadline.CancelAsync();
            firstAttempt.ReleaseSave();
            secondAttempt.ReleaseSave();

            // Observe both commands before disposing their contexts, preserving
            // an earlier assertion failure rather than replacing it in cleanup.
            await completion.ContinueWith(
                static finished => { _ = finished.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static ServiceProvider CreateCoordinatedProvider(NpgsqlDataSource dataSource)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dataSource);
        services.AddScoped<PublicationAttempt>();
        services.AddScoped<TimeProvider>(
            static provider => provider.GetRequiredService<PublicationAttempt>().Clock);
        services.AddHybridCache(options =>
        {
            options.MaximumKeyLength = 512;
            options.MaximumPayloadBytes = 2 * 1024 * 1024;
        });
        services.AddCatalogApplication();
        services.AddCatalogInfrastructure();

        // Wrap the real implementation in this test provider only. The wrapper
        // observes and schedules calls; it never simulates a persistence result.
        services.AddScoped<CatalogUnitOfWork>();
        services.Replace(ServiceDescriptor.Scoped<ICatalogUnitOfWork, CoordinatedUnitOfWork>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static void AssertAttemptReady(PublicationAttempt attempt, ProductId expectedId)
    {
        Assert.Equal(1, attempt.SaveCalls);
        Assert.Equal(ProductStatus.Draft, attempt.OriginalStatus);
        Assert.Equal(1L, attempt.OriginalVersion);
        Assert.Equal(2L, attempt.PendingVersion);
        Assert.NotNull(attempt.Product);
        Assert.Equal(expectedId, attempt.Product.Id);
        Assert.Equal(ProductStatus.Published, attempt.Product.Status);
        Assert.Equal(attempt.Clock.GetUtcNow(), attempt.Product.PublishedAtUtc);
        Assert.Equal(2, attempt.Product.Variants.Count);
        Assert.All(attempt.Product.Variants, variant =>
        {
            Assert.Equal(ProductVariantStatus.Active, variant.Status);
            Assert.Equal(attempt.Clock.GetUtcNow(), variant.ActivatedAtUtc);
        });
        Assert.Equal(3, attempt.Product.DomainEvents.Count);
    }

    private sealed class PublicationAttempt
    {
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PublicationAttempt()
        {
            Clock = new FakeTimeProvider(PublicationTime);
        }

        public FakeTimeProvider Clock { get; }

        public Task Ready => _ready.Task;

        public int SaveCalls { get; set; }

        public ProductStatus OriginalStatus { get; set; }

        public long OriginalVersion { get; set; }

        public long PendingVersion { get; set; }

        public Product? Product { get; set; }

        public CatalogOptimisticConcurrencyException? ConcurrencyFailure { get; set; }

        public async Task AwaitSaveReleaseAsync(CancellationToken cancellationToken)
        {
            _ = _ready.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void ReleaseSave()
        {
            _ = _release.TrySetResult();
        }
    }

    private sealed class CoordinatedUnitOfWork(
        CatalogUnitOfWork inner,
        CatalogDbContext dbContext,
        CatalogDomainEventTracker domainEventTracker,
        PublicationAttempt attempt) : ICatalogUnitOfWork
    {
        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            attempt.SaveCalls++;
            var record = Assert.Single(dbContext.ProductRecords.Local);
            attempt.OriginalStatus = dbContext.Entry(record)
                .Property(product => product.Status).OriginalValue;
            attempt.OriginalVersion = dbContext.Entry(record)
                .Property(product => product.Version).OriginalValue;
            attempt.PendingVersion = record.Version;
            attempt.Product = Assert.Single(domainEventTracker.TrackedProducts);
            await attempt.AwaitSaveReleaseAsync(cancellationToken);

            try
            {
                await inner.SaveChangesAsync(cancellationToken);
            }
            catch (CatalogOptimisticConcurrencyException exception)
            {
                attempt.ConcurrencyFailure = exception;
                throw;
            }
        }
    }

    private static async Task<Result<PublishProductResponse>> DispatchAsync(
        IServiceProvider services,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var dispatcher = services.GetRequiredService<ICommandDispatcher>();
        return await dispatcher.DispatchAsync(
            new PublishProductCommand(productId), cancellationToken);
    }

    private static async Task<Product> SeedDraftAsync(
        ServiceProvider provider)
    {
        var product = Product.CreateDraft(
            ProductName.Create("Publication Concurrency Product").Value,
            ProductSlug.Create($"ecp11g3-race-{Guid.CreateVersion7():N}").Value,
            ProductDescription.Empty,
            PublicationTime.AddMinutes(-2));

        var option = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0);
        Assert.True(option.IsSuccess, option.Error?.Code);

        AddDraftVariant(product, option.Value, "Black");
        AddDraftVariant(product, option.Value, "White");

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
