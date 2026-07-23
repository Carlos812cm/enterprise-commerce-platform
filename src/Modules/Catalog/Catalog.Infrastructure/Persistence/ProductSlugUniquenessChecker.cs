using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ProductSlugUniquenessChecker(
    CatalogDbContext dbContext)
    : IProductSlugUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(
        ProductSlug slug,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return !await dbContext.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Slug == slug,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
