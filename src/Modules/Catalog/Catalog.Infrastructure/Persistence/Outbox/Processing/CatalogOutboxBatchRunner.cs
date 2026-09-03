namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed class CatalogOutboxBatchRunner :
    ICatalogOutboxBatchProcessor
{
    private readonly CatalogOutboxStore _store;

    private readonly CatalogOutboxMessageProcessor
        _messageProcessor;

    public CatalogOutboxBatchRunner(
        CatalogOutboxStore store,
        CatalogOutboxMessageProcessor messageProcessor)
    {
        ArgumentNullException.ThrowIfNull(
            store);

        ArgumentNullException.ThrowIfNull(
            messageProcessor);

        _store = store;

        _messageProcessor =
            messageProcessor;
    }

    public async ValueTask<CatalogOutboxBatchResult>
        RunAsync(
            string workerId,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workerId);

        var claimedMessages =
            await _store.ClaimPendingAsync(
                workerId,
                batchSize,
                leaseDuration,
                cancellationToken);

        if (claimedMessages.Length == 0)
        {
            return CatalogOutboxBatchResult.Empty;
        }

        var processingTasks =
            claimedMessages
                .Select(
                    message =>
                        _messageProcessor
                            .ProcessAsync(
                                message,
                                cancellationToken)
                            .AsTask())
                .ToArray();

        var results =
            await Task.WhenAll(
                processingTasks);

        var processedCount = 0;
        var retryScheduledCount = 0;
        var deadLetteredCount = 0;
        var leaseLostCount = 0;

        foreach (var result in results)
        {
            switch (result.Outcome)
            {
                case CatalogOutboxProcessOutcome.Processed:
                    processedCount++;
                    break;

                case CatalogOutboxProcessOutcome.RetryScheduled:
                    retryScheduledCount++;
                    break;

                case CatalogOutboxProcessOutcome.DeadLettered:
                    deadLetteredCount++;
                    break;

                case CatalogOutboxProcessOutcome.LeaseLost:
                    leaseLostCount++;
                    break;

                default:
                    throw new InvalidOperationException(
                        "The Catalog Outbox processor returned an unsupported outcome.");
            }
        }

        return new CatalogOutboxBatchResult(
            ClaimedCount:
                claimedMessages.Length,
            ProcessedCount:
                processedCount,
            RetryScheduledCount:
                retryScheduledCount,
            DeadLetteredCount:
                deadLetteredCount,
            LeaseLostCount:
                leaseLostCount);
    }
}
