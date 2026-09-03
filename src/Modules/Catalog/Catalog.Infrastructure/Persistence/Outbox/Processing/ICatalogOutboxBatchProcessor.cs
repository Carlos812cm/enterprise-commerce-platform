namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

public interface ICatalogOutboxBatchProcessor
{
    ValueTask<CatalogOutboxBatchResult> RunAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}
