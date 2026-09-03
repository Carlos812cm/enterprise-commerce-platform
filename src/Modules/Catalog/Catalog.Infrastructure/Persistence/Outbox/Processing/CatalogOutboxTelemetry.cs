using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal static class CatalogOutboxTelemetry
{
    internal const string MeterName =
        CatalogOutboxActivity.ActivitySourceName;

    internal const string OutcomeTagName =
        "catalog.outbox.outcome";

    internal const string MessageOutcomeCounterName =
        "catalog.outbox.message.outcomes";

    internal const string ProcessingDurationHistogramName =
        "catalog.outbox.processing.duration";

    internal const string ProcessedOutcome =
        "processed";

    internal const string RetryScheduledOutcome =
        "retry_scheduled";

    internal const string DeadLetteredOutcome =
        "dead_lettered";

    internal const string LeaseLostOutcome =
        "lease_lost";

    private static readonly Meter OutboxMeter =
        new(MeterName);

    private static readonly Counter<long> MessageOutcomes =
        OutboxMeter.CreateCounter<long>(
            MessageOutcomeCounterName,
            unit: "{message}",
            description:
                "Count of Catalog Outbox message processing outcomes.");

    private static readonly Histogram<double> ProcessingDuration =
        OutboxMeter.CreateHistogram<double>(
            ProcessingDurationHistogramName,
            unit: "s",
            description:
                "Catalog Outbox message processing duration.");

    public static void RecordCompletion(
        Activity? activity,
        CatalogOutboxProcessOutcome outcome,
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "The processing duration cannot be negative.");
        }

        var outcomeName =
            GetOutcomeName(
                outcome);

        if (activity is not null)
        {
            activity.SetTag(
                OutcomeTagName,
                outcomeName);

            if (outcome is not
                CatalogOutboxProcessOutcome.Processed)
            {
                activity.SetStatus(
                    ActivityStatusCode.Error);
            }
        }

        var tags =
            new TagList();

        tags.Add(
            OutcomeTagName,
            outcomeName);

        MessageOutcomes.Add(
            1,
            tags);

        ProcessingDuration.Record(
            duration.TotalSeconds,
            tags);
    }

    private static string GetOutcomeName(
        CatalogOutboxProcessOutcome outcome)
    {
        return outcome switch
        {
            CatalogOutboxProcessOutcome.Processed =>
                ProcessedOutcome,

            CatalogOutboxProcessOutcome.RetryScheduled =>
                RetryScheduledOutcome,

            CatalogOutboxProcessOutcome.DeadLettered =>
                DeadLetteredOutcome,

            CatalogOutboxProcessOutcome.LeaseLost =>
                LeaseLostOutcome,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(outcome),
                    outcome,
                    "The Catalog Outbox outcome is not supported.")
        };
    }
}
