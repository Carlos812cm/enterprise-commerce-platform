using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Caching;

internal interface IStorefrontProductCacheInvalidationBroadcaster
{
    ValueTask BroadcastBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken);

    ValueTask BroadcastAllAsync(
        CancellationToken cancellationToken);
}
