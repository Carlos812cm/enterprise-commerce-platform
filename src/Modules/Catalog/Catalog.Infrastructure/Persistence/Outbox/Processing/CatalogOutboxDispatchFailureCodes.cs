namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal static class CatalogOutboxDispatchFailureCodes
{
    public const string CacheInvalidationFailed =
        "catalog.outbox.cache-invalidation-failed";

    public const string CacheInvalidationBroadcastFailed =
        "catalog.outbox.cache-invalidation-broadcast-failed";

    public const string PublisherFailed =
        "catalog.outbox.publisher-failed";

    public const string InvalidDecodedMessage =
        "catalog.outbox.invalid-decoded-message";
}
