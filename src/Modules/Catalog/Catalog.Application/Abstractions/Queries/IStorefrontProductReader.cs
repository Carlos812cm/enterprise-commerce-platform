using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions.Queries;

public interface IStorefrontProductReader
{
    Task<PublishedProductDetailsReadModel?> GetBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken);
}
