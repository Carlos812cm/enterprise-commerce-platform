using Catalog.Application.Products.GetProductById;

namespace Catalog.Application.Abstractions.Queries;

public interface IProductDetailsReader
{
    Task<AdminProductDetailsReadModel?> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken);
}
