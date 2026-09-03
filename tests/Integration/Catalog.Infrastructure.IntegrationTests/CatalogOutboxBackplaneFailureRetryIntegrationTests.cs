using System.Text.Json;
using Catalog.Application.Abstractions.Caching;
using Catalog.Contracts.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Persistence.Outbox;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class
    CatalogOutboxBackplaneFailureRetryIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset PublishedAtUtc =
        new(
            2026,
            8,
            29,
            12,
            0,
            0,
            TimeSpan.Zero);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly CatalogPostgreSqlFixture _fixture;

    public CatalogOutboxBackplaneFailureRetryIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        BackplaneFailureSchedulesRetryWithoutProcessingMessage()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            cancellationToken);

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
            cancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claimedMessage =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    cancellationToken));

        Assert.Equal(
            messageId,
            claimedMessage.Id);

        Assert.Equal(
            0,
            claimedMessage.AttemptCount);

        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var broadcaster =
            new ThrowingBroadcaster();

        var publisher =
            new RecordingPublisher();

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                broadcaster,
                publisher);

        var processor =
            new CatalogOutboxMessageProcessor(
                store,
                dispatcher);

        var result =
            await processor.ProcessAsync(
                claimedMessage,
                cancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.RetryScheduled,
            result.Outcome);

        Assert.Equal(
            CatalogOutboxDispatchFailureCodes
                .CacheInvalidationBroadcastFailed,
            result.ErrorCode);

        Assert.Equal(
            1,
            result.AttemptCount);

        Assert.NotNull(
            result.NextAttemptAtUtc);

        Assert.Equal(
            1,
            cacheInvalidator.CallCount);

        Assert.Equal(
            "enterprise-monitor",
            cacheInvalidator.LastSlug?.Value);

        Assert.Equal(
            1,
            broadcaster.CallCount);

        Assert.Equal(
            "enterprise-monitor",
            broadcaster.LastSlug?.Value);

        Assert.Equal(
            0,
            publisher.CallCount);

        var persistedState =
            await ReadStateAsync(
                dataSource,
                messageId,
                cancellationToken);

        Assert.Equal(
            1,
            persistedState.AttemptCount);

        Assert.True(
            persistedState.RetryScheduledInFuture);

        Assert.True(
            persistedState.LeaseCleared);

        Assert.True(
            persistedState.IsUnprocessed);

        Assert.True(
            persistedState.IsNotDeadLettered);

        Assert.Equal(
            CatalogOutboxDispatchFailureCodes
                .CacheInvalidationBroadcastFailed,
            persistedState.LastErrorCode);

        var immediateClaim =
            await store.ClaimPendingAsync(
                "worker-b",
                1,
                TimeSpan.FromMinutes(1),
                cancellationToken);

        Assert.Empty(
            immediateClaim);
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
                    attempt_count,
                    next_attempt_at_utc IS NOT NULL
                        AND next_attempt_at_utc >
                            CURRENT_TIMESTAMP,
                    lock_owner IS NULL
                        AND locked_until_utc IS NULL,
                    processed_at_utc IS NULL,
                    dead_lettered_at_utc IS NULL,
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
                reader.GetInt32(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
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
        int AttemptCount,
        bool RetryScheduledInFuture,
        bool LeaseCleared,
        bool IsUnprocessed,
        bool IsNotDeadLettered,
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
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "InvalidateAllAsync is not used by this test.");
        }
    }

    private sealed class ThrowingBroadcaster :
        IStorefrontProductCacheInvalidationBroadcaster
    {
        public int CallCount { get; private set; }

        public ProductSlug? LastSlug { get; private set; }

        public ValueTask BroadcastBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            throw new InvalidOperationException(
                "Simulated Redis backplane failure.");
        }

        public ValueTask BroadcastAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "BroadcastAllAsync is not used by this test.");
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
            ArgumentNullException.ThrowIfNull(
                integrationEvent);

            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;

            return ValueTask.FromResult(
                CatalogOutboxDispatchResult.Success);
        }
    }
}
