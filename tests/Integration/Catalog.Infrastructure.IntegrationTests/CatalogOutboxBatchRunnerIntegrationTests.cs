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

public sealed class CatalogOutboxBatchRunnerIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private const string TransientPublisherErrorCode =
        "catalog.outbox.batch-transient";

    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset OccurredAtUtc =
        new(
            2026,
            8,
            30,
            12,
            0,
            0,
            TimeSpan.Zero);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly CatalogPostgreSqlFixture _fixture;

    public CatalogOutboxBatchRunnerIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        MixedBatchAccountsForProcessedRetryAndDeadLetter()
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

        var processedMessageId =
            Guid.CreateVersion7();

        var retryMessageId =
            Guid.CreateVersion7();

        var deadLetterMessageId =
            Guid.CreateVersion7();

        var cachePayload =
            JsonSerializer.Serialize(
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "enterprise-monitor",
                    OccurredAtUtc),
                SerializerOptions);

        var publishedPayload =
            JsonSerializer.Serialize(
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    OccurredAtUtc),
                SerializerOptions);

        await InsertMessageAsync(
            dataSource,
            processedMessageId,
            CatalogOutboxMessageTypes
                .StorefrontProductCacheInvalidateV1,
            cachePayload,
            OccurredAtUtc,
            cancellationToken);

        await InsertMessageAsync(
            dataSource,
            retryMessageId,
            CatalogOutboxMessageTypes
                .ProductPublishedV1,
            publishedPayload,
            OccurredAtUtc.AddSeconds(1),
            cancellationToken);

        await InsertMessageAsync(
            dataSource,
            deadLetterMessageId,
            "catalog.unsupported-batch-message.v1",
            "{}",
            OccurredAtUtc.AddSeconds(2),
            cancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new TransientPublisher();

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var processor =
            new CatalogOutboxMessageProcessor(
                store,
                dispatcher);

        var runner =
            new CatalogOutboxBatchRunner(
                store,
                processor);

        var result =
            await runner.RunAsync(
                "worker-batch-a",
                batchSize: 3,
                leaseDuration:
                    TimeSpan.FromMinutes(1),
                cancellationToken);

        Assert.True(
            result.HasWork);

        Assert.Equal(
            3,
            result.ClaimedCount);

        Assert.Equal(
            1,
            result.ProcessedCount);

        Assert.Equal(
            1,
            result.RetryScheduledCount);

        Assert.Equal(
            1,
            result.DeadLetteredCount);

        Assert.Equal(
            0,
            result.LeaseLostCount);

        Assert.Equal(
            1,
            cacheInvalidator.CallCount);

        Assert.Equal(
            "enterprise-monitor",
            cacheInvalidator.LastSlug?.Value);

        Assert.Equal(
            1,
            publisher.CallCount);

        var processedState =
            await ReadStateAsync(
                dataSource,
                processedMessageId,
                cancellationToken);

        Assert.Equal(
            0,
            processedState.AttemptCount);

        Assert.True(
            processedState.Processed);

        Assert.False(
            processedState.DeadLettered);

        Assert.True(
            processedState.LeaseCleared);

        Assert.False(
            processedState.RetryScheduledInFuture);

        Assert.Null(
            processedState.LastErrorCode);

        var retryState =
            await ReadStateAsync(
                dataSource,
                retryMessageId,
                cancellationToken);

        Assert.Equal(
            1,
            retryState.AttemptCount);

        Assert.False(
            retryState.Processed);

        Assert.False(
            retryState.DeadLettered);

        Assert.True(
            retryState.LeaseCleared);

        Assert.True(
            retryState.RetryScheduledInFuture);

        Assert.Equal(
            TransientPublisherErrorCode,
            retryState.LastErrorCode);

        var deadLetterState =
            await ReadStateAsync(
                dataSource,
                deadLetterMessageId,
                cancellationToken);

        Assert.Equal(
            1,
            deadLetterState.AttemptCount);

        Assert.False(
            deadLetterState.Processed);

        Assert.True(
            deadLetterState.DeadLettered);

        Assert.True(
            deadLetterState.LeaseCleared);

        Assert.False(
            deadLetterState.RetryScheduledInFuture);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .UnsupportedMessageType,
            deadLetterState.LastErrorCode);

        var immediateClaim =
            await store.ClaimPendingAsync(
                "worker-batch-b",
                3,
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
        string messageType,
        string payload,
        DateTimeOffset occurredAtUtc,
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
            occurredAtUtc);

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
                    processed_at_utc IS NOT NULL,
                    dead_lettered_at_utc IS NOT NULL,
                    lock_owner IS NULL
                        AND locked_until_utc IS NULL,
                    next_attempt_at_utc IS NOT NULL
                        AND next_attempt_at_utc >
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
        bool Processed,
        bool DeadLettered,
        bool LeaseCleared,
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

    private sealed class TransientPublisher :
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
                CatalogOutboxDispatchResult
                    .TransientFailure(
                        TransientPublisherErrorCode));
        }
    }
}
