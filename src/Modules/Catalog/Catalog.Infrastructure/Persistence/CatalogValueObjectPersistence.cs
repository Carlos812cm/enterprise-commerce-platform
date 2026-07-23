using Catalog.Domain.Products;
using Commerce.Domain;

namespace Catalog.Infrastructure.Persistence;

internal static class CatalogValueObjectPersistence
{
    public static ProductName CreateProductName(
        string value)
    {
        return GetRequiredValue(
            ProductName.Create(value),
            nameof(ProductName));
    }

    public static ProductSlug CreateProductSlug(
        string value)
    {
        return GetRequiredValue(
            ProductSlug.Create(value),
            nameof(ProductSlug));
    }

    public static ProductDescription CreateProductDescription(
        string value)
    {
        return GetRequiredValue(
            ProductDescription.Create(value),
            nameof(ProductDescription));
    }

    private static T GetRequiredValue<T>(
        Result<T> result,
        string typeName)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"The persisted value could not be rehydrated as {typeName}. Domain error: {result.Error?.Code}.");
        }

        return result.Value;
    }
}
