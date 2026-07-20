using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions.Persistence;

public interface IProductRepository
{
    void Add(Product product);
}
