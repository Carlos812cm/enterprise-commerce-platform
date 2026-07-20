using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions.Persistence;

public interface IProductSlugUniquenessChecker
{
    Task<bool> IsUniqueAsync(
        ProductSlug slug,
        CancellationToken cancellationToken);
}
