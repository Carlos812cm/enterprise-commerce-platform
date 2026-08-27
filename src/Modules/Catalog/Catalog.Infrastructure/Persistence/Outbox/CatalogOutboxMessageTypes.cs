namespace Catalog.Infrastructure.Persistence.Outbox;

internal static class CatalogOutboxMessageTypes
{
    public const string StorefrontProductCacheInvalidateV1 =
        "catalog.storefront-product-cache-invalidate.v1";

    public const string ProductPublishedV1 =
        "catalog.product-published.v1";
}
