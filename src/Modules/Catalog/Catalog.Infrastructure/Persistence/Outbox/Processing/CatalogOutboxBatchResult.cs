namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

public sealed record CatalogOutboxBatchResult(
    int ClaimedCount,
    int ProcessedCount,
    int RetryScheduledCount,
    int DeadLetteredCount,
    int LeaseLostCount)
{
    public static CatalogOutboxBatchResult Empty { get; } =
        new(
            ClaimedCount: 0,
            ProcessedCount: 0,
            RetryScheduledCount: 0,
            DeadLetteredCount: 0,
            LeaseLostCount: 0);

    public bool HasWork =>
        ClaimedCount > 0;
}
