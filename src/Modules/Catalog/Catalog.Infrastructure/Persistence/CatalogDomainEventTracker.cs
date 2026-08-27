using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Persistence;

internal sealed class CatalogDomainEventTracker
{
    private readonly Dictionary<ProductId, Product> _products =
        [];

    public IReadOnlyCollection<Product> TrackedProducts =>
        _products.Values;

    public void Track(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        _products[product.Id] =
            product;
    }
}
