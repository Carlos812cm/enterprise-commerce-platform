using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository(
    CatalogDbContext dbContext)
    : IProductRepository
{
    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        dbContext.Products.Add(product);
    }
}
