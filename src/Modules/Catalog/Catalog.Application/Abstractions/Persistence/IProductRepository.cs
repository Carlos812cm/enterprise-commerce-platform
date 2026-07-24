using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions.Persistence;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken);

    void Add(Product product);

    void Update(Product product);
}
