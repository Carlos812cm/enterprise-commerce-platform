using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxTelemetryTests
{
    [Fact]
    public void
        RecordsBoundedOutcomeMetricsAndMarksNonSuccessActivitiesAsErrors()
    {
        var counterMeasurements =
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

                counterMeasurements.AddOrUpdate(
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
                                .AllDataAndRecorded
            };

        ActivitySource.AddActivityListener(
            activityListener);

        TelemetryCase[] cases =
        [
            new(
                CatalogOutboxProcessOutcome.Processed,
                CatalogOutboxTelemetry.ProcessedOutcome,
                ActivityStatusCode.Unset),

            new(
                CatalogOutboxProcessOutcome.RetryScheduled,
                CatalogOutboxTelemetry.RetryScheduledOutcome,
                ActivityStatusCode.Error),

            new(
                CatalogOutboxProcessOutcome.DeadLettered,
                CatalogOutboxTelemetry.DeadLetteredOutcome,
                ActivityStatusCode.Error),

            new(
                CatalogOutboxProcessOutcome.LeaseLost,
                CatalogOutboxTelemetry.LeaseLostOutcome,
                ActivityStatusCode.Error)
        ];

        for (var index = 0;
            index < cases.Length;
            index++)
        {
            var testCase =
                cases[index];

            using var activityScope =
                CatalogOutboxActivity.Start(
                    CreateMessage());

            var activity =
                Assert.IsType<Activity>(
                    activityScope.Activity);

            var duration =
                TimeSpan.FromMilliseconds(
                    25 + index);

            CatalogOutboxTelemetry.RecordCompletion(
                activity,
                testCase.Outcome,
                duration);

            var outcomeTag =
                Assert.IsType<string>(
                    activity.GetTagItem(
                        CatalogOutboxTelemetry
                            .OutcomeTagName));

            Assert.Equal(
                testCase.ExpectedOutcomeName,
                outcomeTag);

            Assert.Equal(
                testCase.ExpectedStatus,
                activity.Status);
        }

        foreach (var testCase in cases)
        {
            Assert.True(
                counterMeasurements.TryGetValue(
                    testCase.ExpectedOutcomeName,
                    out var count));

            Assert.True(
                count >= 1);

            Assert.True(
                durationMeasurements.TryGetValue(
                    testCase.ExpectedOutcomeName,
                    out var durationSeconds));

            Assert.True(
                durationSeconds > 0);
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                CatalogOutboxTelemetry.RecordCompletion(
                    null,
                    CatalogOutboxProcessOutcome.Processed,
                    TimeSpan.FromTicks(-1)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                CatalogOutboxTelemetry.RecordCompletion(
                    null,
                    (CatalogOutboxProcessOutcome)999,
                    TimeSpan.Zero));
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
            "A Catalog Outbox metric did not contain the bounded outcome tag.");
    }

    private static ClaimedCatalogOutboxMessage
        CreateMessage()
    {
        var now =
            DateTimeOffset.UtcNow;

        return new ClaimedCatalogOutboxMessage(
            Guid.CreateVersion7(),
            "catalog.telemetry-test.v1",
            "{}",
            now,
            now,
            AttemptCount: 0,
            LeaseOwner:
                "worker-telemetry-test",
            LockedUntilUtc:
                now.AddMinutes(1),
            TraceParent: null,
            TraceState: null);
    }

    private sealed record TelemetryCase(
        CatalogOutboxProcessOutcome Outcome,
        string ExpectedOutcomeName,
        ActivityStatusCode ExpectedStatus);
}
