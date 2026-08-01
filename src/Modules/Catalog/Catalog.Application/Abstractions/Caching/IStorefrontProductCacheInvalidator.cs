using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions.Caching;

public interface IStorefrontProductCacheInvalidator
{
    ValueTask InvalidateBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken);

    ValueTask InvalidateAllAsync(
        CancellationToken cancellationToken);
}
