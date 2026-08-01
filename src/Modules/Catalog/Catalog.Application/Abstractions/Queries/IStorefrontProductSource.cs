using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions.Queries;

public interface IStorefrontProductSource
{
    Task<PublishedProductDetailsReadModel?> GetBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken);
}
