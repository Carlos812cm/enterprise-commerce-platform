namespace Catalog.Infrastructure.Caching;

internal static class StorefrontProductCacheInvalidationBackplane
{
    public const string ChannelName =
        "commerce.catalog.storefront-cache-invalidation.v1";

    public const string InvalidateAllMessage =
        "*";
}
