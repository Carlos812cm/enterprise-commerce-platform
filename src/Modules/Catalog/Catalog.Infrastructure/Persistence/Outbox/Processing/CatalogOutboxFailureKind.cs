namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal enum CatalogOutboxFailureKind
{
    Transient = 0,
    Permanent = 1
}
