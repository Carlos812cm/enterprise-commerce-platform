using System.Text.Json;
using Catalog.Application.Abstractions.Caching;
using Catalog.Contracts.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Outbox;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxMessageProcessorTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset PublishedAtUtc =
        new(
            2026,
            8,
            28,
            12,
            0,
            0,
            TimeSpan.Zero);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly CatalogPostgreSqlFixture _fixture;

    public CatalogOutboxMessageProcessorTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        SuccessfulDispatchMarksMessageProcessed()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        var payload =
            JsonSerializer.Serialize(
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc),
                SerializerOptions);

        await InsertMessageAsync(
            dataSource,
            messageId,
            CatalogOutboxMessageTypes
                .StorefrontProductCacheInvalidateV1,
            payload,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new StubPublisher(
                CatalogOutboxDispatchResult.Success);

        var processor =
            CreateProcessor(
                store,
                cacheInvalidator,
                publisher);

        var result =
            await processor.ProcessAsync(
                claim,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.Processed,
            result.Outcome);

        Assert.Null(
            result.ErrorCode);

        Assert.Equal(
            1,
            cacheInvalidator.CallCount);

        Assert.Equal(
            "enterprise-monitor",
            cacheInvalidator.LastSlug?.Value);

        Assert.Equal(
            0,
            publisher.CallCount);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.True(
            state.Processed);

        Assert.False(
            state.DeadLettered);

        Assert.True(
            state.LeaseCleared);

        Assert.Equal(
            0,
            state.AttemptCount);

        Assert.Null(
            state.LastErrorCode);
    }

    [Fact]
    public async Task
        UnsupportedMessageTypeIsDeadLetteredWithoutDispatch()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        await InsertMessageAsync(
            dataSource,
            messageId,
            "catalog.unknown.v1",
            "{}",
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new StubPublisher(
                CatalogOutboxDispatchResult.Success);

        var processor =
            CreateProcessor(
                store,
                cacheInvalidator,
                publisher);

        var result =
            await processor.ProcessAsync(
                claim,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.DeadLettered,
            result.Outcome);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .UnsupportedMessageType,
            result.ErrorCode);

        Assert.Equal(
            1,
            result.AttemptCount);

        Assert.Equal(
            0,
            cacheInvalidator.CallCount);

        Assert.Equal(
            0,
            publisher.CallCount);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.False(
            state.Processed);

        Assert.True(
            state.DeadLettered);

        Assert.True(
            state.LeaseCleared);

        Assert.Equal(
            1,
            state.AttemptCount);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .UnsupportedMessageType,
            state.LastErrorCode);
    }

    [Fact]
    public async Task
        TransientDispatchFailureSchedulesRetry()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        var payload =
            JsonSerializer.Serialize(
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc),
                SerializerOptions);

        await InsertMessageAsync(
            dataSource,
            messageId,
            CatalogOutboxMessageTypes.ProductPublishedV1,
            payload,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new StubPublisher(
                CatalogOutboxDispatchResult
                    .TransientFailure(
                        "catalog.rabbitmq.unavailable"));

        var processor =
            CreateProcessor(
                store,
                cacheInvalidator,
                publisher);

        var result =
            await processor.ProcessAsync(
                claim,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.RetryScheduled,
            result.Outcome);

        Assert.Equal(
            "catalog.rabbitmq.unavailable",
            result.ErrorCode);

        Assert.Equal(
            1,
            result.AttemptCount);

        Assert.NotNull(
            result.NextAttemptAtUtc);

        Assert.Equal(
            0,
            cacheInvalidator.CallCount);

        Assert.Equal(
            1,
            publisher.CallCount);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.False(
            state.Processed);

        Assert.False(
            state.DeadLettered);

        Assert.True(
            state.LeaseCleared);

        Assert.True(
            state.RetryScheduledInFuture);

        Assert.Equal(
            1,
            state.AttemptCount);

        Assert.Equal(
            "catalog.rabbitmq.unavailable",
            state.LastErrorCode);

        var immediateClaim =
            await store.ClaimPendingAsync(
                "worker-b",
                1,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        Assert.Empty(
            immediateClaim);
    }

    [Fact]
    public async Task
        PermanentDispatchFailureDeadLettersMessage()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        var payload =
            JsonSerializer.Serialize(
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc),
                SerializerOptions);

        await InsertMessageAsync(
            dataSource,
            messageId,
            CatalogOutboxMessageTypes.ProductPublishedV1,
            payload,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new StubPublisher(
                CatalogOutboxDispatchResult
                    .PermanentFailure(
                        "catalog.rabbitmq.unroutable"));

        var processor =
            CreateProcessor(
                store,
                cacheInvalidator,
                publisher);

        var result =
            await processor.ProcessAsync(
                claim,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.DeadLettered,
            result.Outcome);

        Assert.Equal(
            "catalog.rabbitmq.unroutable",
            result.ErrorCode);

        Assert.Equal(
            1,
            result.AttemptCount);

        Assert.Equal(
            0,
            cacheInvalidator.CallCount);

        Assert.Equal(
            1,
            publisher.CallCount);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.False(
            state.Processed);

        Assert.True(
            state.DeadLettered);

        Assert.True(
            state.LeaseCleared);

        Assert.Equal(
            1,
            state.AttemptCount);

        Assert.Equal(
            "catalog.rabbitmq.unroutable",
            state.LastErrorCode);

        var laterClaim =
            await store.ClaimPendingAsync(
                "worker-b",
                1,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        Assert.Empty(
            laterClaim);
    }

    private static CatalogOutboxMessageProcessor CreateProcessor(
        CatalogOutboxStore store,
        RecordingCacheInvalidator cacheInvalidator,
        StubPublisher publisher)
    {
        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        return new CatalogOutboxMessageProcessor(
            store,
            dispatcher);
    }

    private static async Task ResetOutboxAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                TRUNCATE TABLE
                    catalog.outbox_messages;
                """);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertMessageAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        string messageType,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                INSERT INTO catalog.outbox_messages (
                    id,
                    type,
                    payload,
                    occurred_at_utc
                )
                VALUES (
                    @id,
                    @type,
                    CAST(@payload AS jsonb),
                    @occurred_at_utc
                );
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        command.Parameters.AddWithValue(
            "type",
            messageType);

        command.Parameters.AddWithValue(
            "payload",
            payload);

        command.Parameters.AddWithValue(
            "occurred_at_utc",
            PublishedAtUtc);

        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(
                cancellationToken));
    }

    private static async Task<OutboxState> ReadStateAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                SELECT
                    processed_at_utc IS NOT NULL,
                    dead_lettered_at_utc IS NOT NULL,
                    lock_owner IS NULL
                        AND locked_until_utc IS NULL,
                    attempt_count,
                    next_attempt_at_utc >
                        CURRENT_TIMESTAMP,
                    last_error_code
                FROM catalog.outbox_messages
                WHERE id = @id;
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        var state =
            new OutboxState(
                reader.GetBoolean(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5));

        Assert.False(
            await reader.ReadAsync(
                cancellationToken));

        return state;
    }

    private sealed record OutboxState(
        bool Processed,
        bool DeadLettered,
        bool LeaseCleared,
        int AttemptCount,
        bool RetryScheduledInFuture,
        string? LastErrorCode);

    private sealed class RecordingCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        public int CallCount { get; private set; }

        public ProductSlug? LastSlug { get; private set; }

        public ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "InvalidateAllAsync is not used by this test.");
        }
    }

    private sealed class StubPublisher :
        ICatalogProductPublishedPublisher
    {
        private readonly CatalogOutboxDispatchResult _result;

        public StubPublisher(
            CatalogOutboxDispatchResult result)
        {
            ArgumentNullException.ThrowIfNull(
                result);

            _result = result;
        }

        public int CallCount { get; private set; }

        public ValueTask<CatalogOutboxDispatchResult>
            PublishAsync(
                Guid outboxMessageId,
                ProductPublishedIntegrationEventV1 integrationEvent,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            return ValueTask.FromResult(
                _result);
        }
    }
}
