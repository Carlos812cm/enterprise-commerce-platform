namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal enum CatalogOutboxProcessOutcome
{
    Processed = 0,
    RetryScheduled = 1,
    DeadLettered = 2,
    LeaseLost = 3
}
