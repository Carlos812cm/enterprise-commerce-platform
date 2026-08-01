using Catalog.Application.Products.GetPublishedProductBySlug;

namespace Catalog.Infrastructure.Caching;

internal sealed record StorefrontProductCacheEntry(PublishedProductDetailsReadModel? Product);
