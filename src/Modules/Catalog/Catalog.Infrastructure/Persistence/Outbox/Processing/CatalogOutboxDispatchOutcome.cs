namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal enum CatalogOutboxDispatchOutcome
{
    Success = 0,
    TransientFailure = 1,
    PermanentFailure = 2
}
