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

public sealed class
    CatalogOutboxPostDispatchLeaseLossIntegrationTests :
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

    public CatalogOutboxPostDispatchLeaseLossIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        EffectCanSucceedBeforeLeaseLossPreventsCompletion()
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
            payload,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var firstClaim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var cacheInvalidator =
            new ReclaimingCacheInvalidator(
                dataSource,
                store,
                messageId,
                firstClaim.LeaseOwner);

        var publisher =
            new RecordingPublisher();

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var processor =
            new CatalogOutboxMessageProcessor(
                store,
                dispatcher);

        var result =
            await processor.ProcessAsync(
                firstClaim,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.LeaseLost,
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

        var secondClaim =
            Assert.IsType<ClaimedCatalogOutboxMessage>(
                cacheInvalidator.ReclaimedMessage);

        Assert.Equal(
            messageId,
            secondClaim.Id);

        Assert.NotEqual(
            firstClaim.LeaseOwner,
            secondClaim.LeaseOwner);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.False(
            state.Processed);

        Assert.False(
            state.DeadLettered);

        Assert.Equal(
            0,
            state.AttemptCount);

        Assert.Equal(
            secondClaim.LeaseOwner,
            state.LockOwner);

        Assert.True(
            state.LeaseActive);

        Assert.True(
            await store.MarkProcessedAsync(
                secondClaim.Id,
                secondClaim.LeaseOwner,
                TestContext.Current.CancellationToken));
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
            CatalogOutboxMessageTypes
                .StorefrontProductCacheInvalidateV1);

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

    private static async Task<OutboxState>
        ReadStateAsync(
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
                    attempt_count,
                    lock_owner,
                    locked_until_utc >
                        CURRENT_TIMESTAMP
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
                reader.GetInt32(2),
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3),
                reader.GetBoolean(4));

        Assert.False(
            await reader.ReadAsync(
                cancellationToken));

        return state;
    }

    private sealed record OutboxState(
        bool Processed,
        bool DeadLettered,
        int AttemptCount,
        string? LockOwner,
        bool LeaseActive);

    private sealed class ReclaimingCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly CatalogOutboxStore _store;
        private readonly Guid _messageId;
        private readonly string _originalLeaseOwner;

        public ReclaimingCacheInvalidator(
            NpgsqlDataSource dataSource,
            CatalogOutboxStore store,
            Guid messageId,
            string originalLeaseOwner)
        {
            ArgumentNullException.ThrowIfNull(
                dataSource);

            ArgumentNullException.ThrowIfNull(
                store);

            ArgumentException.ThrowIfNullOrWhiteSpace(
                originalLeaseOwner);

            _dataSource = dataSource;
            _store = store;
            _messageId = messageId;
            _originalLeaseOwner = originalLeaseOwner;
        }

        public int CallCount { get; private set; }

        public ProductSlug? LastSlug { get; private set; }

        public ClaimedCatalogOutboxMessage?
            ReclaimedMessage
        { get; private set; }

        public async ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            await ExpireOriginalLeaseAsync(
                cancellationToken);

            ReclaimedMessage =
                Assert.Single(
                    await _store.ClaimPendingAsync(
                        "worker-b",
                        1,
                        TimeSpan.FromMinutes(1),
                        cancellationToken));
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "InvalidateAllAsync is not used by this test.");
        }

        private async Task ExpireOriginalLeaseAsync(
            CancellationToken cancellationToken)
        {
            await using var command =
                _dataSource.CreateCommand(
                    """
                    UPDATE catalog.outbox_messages
                    SET locked_until_utc =
                        CURRENT_TIMESTAMP -
                        INTERVAL '1 second'
                    WHERE id = @id
                      AND lock_owner = @lease_owner;
                    """);

            command.Parameters.AddWithValue(
                "id",
                _messageId);

            command.Parameters.AddWithValue(
                "lease_owner",
                _originalLeaseOwner);

            Assert.Equal(
                1,
                await command.ExecuteNonQueryAsync(
                    cancellationToken));
        }
    }

    private sealed class RecordingPublisher :
        ICatalogProductPublishedPublisher
    {
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
                CatalogOutboxDispatchResult.Success);
        }
    }
}
