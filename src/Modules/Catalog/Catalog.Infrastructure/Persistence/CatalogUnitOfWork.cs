using Catalog.Application.Abstractions.Persistence;
using Catalog.Infrastructure.Persistence.Outbox;
using Commerce.Domain;

namespace Catalog.Infrastructure.Persistence;

internal sealed class CatalogUnitOfWork(
    CatalogDbContext dbContext,
    CatalogDomainEventTracker domainEventTracker)
    : ICatalogUnitOfWork
{
    private readonly HashSet<IDomainEvent> _stagedDomainEvents =
        new(ReferenceEqualityComparer.Instance);

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        StageDomainEvents();

        _ = await dbContext
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);

        ClearCommittedDomainEvents();
    }

    private void StageDomainEvents()
    {
        foreach (var product in
            domainEventTracker.TrackedProducts)
        {
            foreach (var domainEvent in
                product.DomainEvents)
            {
                if (_stagedDomainEvents.Contains(
                        domainEvent))
                {
                    continue;
                }

                var outboxMessages =
                    CatalogOutboxProjector.Project(
                        domainEvent);

                if (outboxMessages.Length > 0)
                {
                    dbContext.OutboxMessages.AddRange(
                        outboxMessages);
                }

                _stagedDomainEvents.Add(
                    domainEvent);
            }
        }
    }

    private void ClearCommittedDomainEvents()
    {
        foreach (var product in
            domainEventTracker.TrackedProducts)
        {
            _ = product.DequeueDomainEvents();
        }

        _stagedDomainEvents.Clear();
    }
}
