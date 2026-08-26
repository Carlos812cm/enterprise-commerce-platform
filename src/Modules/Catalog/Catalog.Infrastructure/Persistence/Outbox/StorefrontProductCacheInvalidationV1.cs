namespace Catalog.Infrastructure.Persistence.Outbox;

internal sealed record StorefrontProductCacheInvalidationV1(
    Guid ProductId,
    string Slug,
    DateTimeOffset PublishedAtUtc);
