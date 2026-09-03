using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    CatalogOutboxProcessorTelemetryIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private const string ProcessedTraceId =
        "11111111111111111111111111111111";

    private const string RetryTraceId =
        "22222222222222222222222222222222";

    private const string ProcessedTraceParent =
        "00-11111111111111111111111111111111-1111111111111111-01";

    private const string RetryTraceParent =
        "00-22222222222222222222222222222222-2222222222222222-01";

    private const string TraceState =
        "vendor=value";

    private const string TransientErrorCode =
        "catalog.outbox.telemetry-transient";

    private static readonly Guid ProductId =
        Guid.Parse(
            "019c2b42-3e1e-782f-b117-891e68dc89eb");

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly CatalogPostgreSqlFixture _fixture;

    public CatalogOutboxProcessorTelemetryIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        ProcessorEmitsProcessedAndRetryTelemetryFromRealStateTransitions()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var outcomeCounts =
            new ConcurrentDictionary<string, long>(
                StringComparer.Ordinal);

        var durationMeasurements =
            new ConcurrentDictionary<string, double>(
                StringComparer.Ordinal);

        using var meterListener =
            new MeterListener();

        meterListener.InstrumentPublished =
            static (instrument, listener) =>
            {
                if (string.Equals(
                    instrument.Meter.Name,
                    CatalogOutboxTelemetry.MeterName,
                    StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(
                        instrument);
                }
            };

        meterListener.SetMeasurementEventCallback<long>(
            (
                instrument,
                measurement,
                tags,
                state) =>
            {
                if (!string.Equals(
                    instrument.Name,
                    CatalogOutboxTelemetry
                        .MessageOutcomeCounterName,
                    StringComparison.Ordinal))
                {
                    return;
                }

                var outcome =
                    GetRequiredOutcome(
                        tags);

                outcomeCounts.AddOrUpdate(
                    outcome,
                    measurement,
                    (key, current) =>
                        current + measurement);
            });

        meterListener.SetMeasurementEventCallback<double>(
            (
                instrument,
                measurement,
                tags,
                state) =>
            {
                if (!string.Equals(
                    instrument.Name,
                    CatalogOutboxTelemetry
                        .ProcessingDurationHistogramName,
                    StringComparison.Ordinal))
                {
                    return;
                }

                var outcome =
                    GetRequiredOutcome(
                        tags);

                durationMeasurements[outcome] =
                    measurement;
            });

        meterListener.Start();

        var stoppedActivities =
            new ConcurrentDictionary<
                string,
                ActivitySnapshot>(
                    StringComparer.Ordinal);

        using var activityListener =
            new ActivityListener
            {
                ShouldListenTo =
                    source =>
                        source.Name ==
                        CatalogOutboxActivity
                            .ActivitySourceName,

                Sample =
                    static (
                        ref ActivityCreationOptions<ActivityContext> _) =>
                            ActivitySamplingResult
                                .AllDataAndRecorded,

                ActivityStopped =
                    activity =>
                    {
                        var outcome =
                            activity.GetTagItem(
                                CatalogOutboxTelemetry
                                    .OutcomeTagName)
                            as string;

                        stoppedActivities[
                            activity.TraceId.ToString()] =
                                new ActivitySnapshot(
                                    activity.Status,
                                    outcome,
                                    activity.Duration);
                    }
            };

        ActivitySource.AddActivityListener(
            activityListener);

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

        var now =
            DateTimeOffset.UtcNow;

        var processedPayload =
            JsonSerializer.Serialize(
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "telemetry-processed",
                    now),
                SerializerOptions);

        var retryPayload =
            JsonSerializer.Serialize(
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "telemetry-retry",
                    now),
                SerializerOptions);

        await InsertMessageAsync(
            dataSource,
            processedMessageId,
            CatalogOutboxMessageTypes
                .StorefrontProductCacheInvalidateV1,
            processedPayload,
            ProcessedTraceParent,
            now,
            cancellationToken);

        await InsertMessageAsync(
            dataSource,
            retryMessageId,
            CatalogOutboxMessageTypes
                .ProductPublishedV1,
            retryPayload,
            RetryTraceParent,
            now.AddMilliseconds(1),
            cancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claimedMessages =
            await store.ClaimPendingAsync(
                "worker-telemetry",
                batchSize: 2,
                leaseDuration:
                    TimeSpan.FromMinutes(1),
                cancellationToken);

        Assert.Equal(
            2,
            claimedMessages.Length);

        var processedMessage =
            claimedMessages.Single(
                candidate =>
                    candidate.Id ==
                    processedMessageId);

        var retryMessage =
            claimedMessages.Single(
                candidate =>
                    candidate.Id ==
                    retryMessageId);

        var dispatcher =
            new CatalogOutboxDispatcher(
                new SuccessfulCacheInvalidator(),
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                new TransientPublisher());

        var processor =
            new CatalogOutboxMessageProcessor(
                store,
                dispatcher);

        var processedResult =
            await processor.ProcessAsync(
                processedMessage,
                cancellationToken);

        var retryResult =
            await processor.ProcessAsync(
                retryMessage,
                cancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.Processed,
            processedResult.Outcome);

        Assert.Equal(
            CatalogOutboxProcessOutcome.RetryScheduled,
            retryResult.Outcome);

        Assert.True(
            stoppedActivities.TryGetValue(
                ProcessedTraceId,
                out var processedActivityValue));

        var processedActivity =
            Assert.IsType<ActivitySnapshot>(
                processedActivityValue);

        Assert.Equal(
            CatalogOutboxTelemetry.ProcessedOutcome,
            processedActivity.Outcome);

        Assert.Equal(
            ActivityStatusCode.Unset,
            processedActivity.Status);

        Assert.True(
            processedActivity.Duration >
                TimeSpan.Zero);

        Assert.True(
            stoppedActivities.TryGetValue(
                RetryTraceId,
                out var retryActivityValue));

        var retryActivity =
            Assert.IsType<ActivitySnapshot>(
                retryActivityValue);

        Assert.Equal(
            CatalogOutboxTelemetry.RetryScheduledOutcome,
            retryActivity.Outcome);

        Assert.Equal(
            ActivityStatusCode.Error,
            retryActivity.Status);

        Assert.True(
            retryActivity.Duration >
                TimeSpan.Zero);

        AssertMetric(
            outcomeCounts,
            durationMeasurements,
            CatalogOutboxTelemetry.ProcessedOutcome);

        AssertMetric(
            outcomeCounts,
            durationMeasurements,
            CatalogOutboxTelemetry.RetryScheduledOutcome);
    }

    private static void AssertMetric(
        ConcurrentDictionary<string, long> outcomeCounts,
        ConcurrentDictionary<string, double> durationMeasurements,
        string outcome)
    {
        Assert.True(
            outcomeCounts.TryGetValue(
                outcome,
                out var count));

        Assert.True(
            count >= 1);

        Assert.True(
            durationMeasurements.TryGetValue(
                outcome,
                out var durationSeconds));

        Assert.True(
            durationSeconds > 0);
    }

    private static string GetRequiredOutcome(
        ReadOnlySpan<
            KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (
                string.Equals(
                    tag.Key,
                    CatalogOutboxTelemetry
                        .OutcomeTagName,
                    StringComparison.Ordinal) &&
                tag.Value is string outcome)
            {
                return outcome;
            }
        }

        throw new InvalidOperationException(
            "A processor metric did not contain the bounded outcome tag.");
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
        string traceParent,
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
                    occurred_at_utc,
                    trace_parent,
                    trace_state
                )
                VALUES (
                    @id,
                    @type,
                    CAST(@payload AS jsonb),
                    @occurred_at_utc,
                    @trace_parent,
                    @trace_state
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

        command.Parameters.AddWithValue(
            "trace_parent",
            traceParent);

        command.Parameters.AddWithValue(
            "trace_state",
            TraceState);

        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(
                cancellationToken));
    }

    private sealed class SuccessfulCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        public ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

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

            return ValueTask.FromResult(
                CatalogOutboxDispatchResult
                    .TransientFailure(
                        TransientErrorCode));
        }
    }

    private sealed record ActivitySnapshot(
        ActivityStatusCode Status,
        string? Outcome,
        TimeSpan Duration);
}
