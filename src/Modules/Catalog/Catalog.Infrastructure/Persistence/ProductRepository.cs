using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository(
    CatalogDbContext dbContext)
    : IProductRepository
{
    private readonly Dictionary<
        ProductId,
        ProductRecord> _trackedRecords = [];

    public async Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken)
    {
        if (productId.IsEmpty)
        {
            throw new ArgumentException(
                "A product identifier cannot be empty.",
                nameof(productId));
        }

        var productIdValue =
            productId.Value;

        var record = await dbContext.ProductRecords
            .AsSplitQuery()
            .Include(
                product =>
                    product.OptionDefinitions)
            .Include(
                product =>
                    product.Variants)
            .ThenInclude(
                variant =>
                    variant.OptionSelections)
            .SingleOrDefaultAsync(
                product =>
                    product.Id ==
                    productIdValue,
                cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        var product =
            ProductPersistenceMapper
                .ToDomain(record);

        _trackedRecords[product.Id] =
            record;

        return product;
    }

    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var record =
            ProductPersistenceMapper
                .ToRecord(product);

        dbContext.ProductRecords.Add(
            record);

        _trackedRecords.Add(
            product.Id,
            record);
    }

    public void Update(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!_trackedRecords.TryGetValue(
                product.Id,
                out var record))
        {
            throw new InvalidOperationException(
                "The product must be loaded by this repository before it can be updated.");
        }

        ProductPersistenceMapper.Apply(
            product,
            record);
    }
}
