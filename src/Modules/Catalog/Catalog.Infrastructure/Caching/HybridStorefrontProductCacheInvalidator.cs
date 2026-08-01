using Catalog.Application.Abstractions.Caching;
using Catalog.Domain.Products;
using Microsoft.Extensions.Caching.Hybrid;

namespace Catalog.Infrastructure.Caching;

internal sealed class
    HybridStorefrontProductCacheInvalidator(
        HybridCache cache)
    : IStorefrontProductCacheInvalidator
{
    public ValueTask InvalidateBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return cache.RemoveByTagAsync(
            StorefrontProductCacheKeys
                .CreateSlugTag(slug),
            cancellationToken);
    }

    public ValueTask InvalidateAllAsync(
        CancellationToken cancellationToken)
    {
        return cache.RemoveByTagAsync(
            StorefrontProductCacheKeys
                .AllProductsTag,
            cancellationToken);
    }
}
