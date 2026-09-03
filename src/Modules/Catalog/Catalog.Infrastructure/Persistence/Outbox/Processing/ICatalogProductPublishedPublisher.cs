using Catalog.Contracts.Products;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal interface ICatalogProductPublishedPublisher
{
    ValueTask<CatalogOutboxDispatchResult> PublishAsync(
        Guid outboxMessageId,
        ProductPublishedIntegrationEventV1 integrationEvent,
        CancellationToken cancellationToken);
}
