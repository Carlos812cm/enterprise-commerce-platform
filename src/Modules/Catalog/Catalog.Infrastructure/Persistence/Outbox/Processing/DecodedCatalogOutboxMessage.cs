using Catalog.Contracts.Products;
using Catalog.Infrastructure.Persistence.Outbox;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal abstract record DecodedCatalogOutboxMessage(
    Guid OutboxMessageId);

internal sealed record DecodedStorefrontProductCacheInvalidation(
    Guid OutboxMessageId,
    StorefrontProductCacheInvalidationV1 Payload)
    : DecodedCatalogOutboxMessage(
        OutboxMessageId);

internal sealed record DecodedProductPublished(
    Guid OutboxMessageId,
    ProductPublishedIntegrationEventV1 Payload)
    : DecodedCatalogOutboxMessage(
        OutboxMessageId);
