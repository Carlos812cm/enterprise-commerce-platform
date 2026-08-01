using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Caching;

internal static class StorefrontProductCacheKeys
{
    private const string ProductKeyPrefix =
        "catalog:storefront:product:v1:";

    private const string ProductSlugTagPrefix =
        "catalog:storefront:slug:";

    public const string AllProductsTag =
        "catalog:storefront:products";

    public static string CreateProductKey(
        ProductSlug slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return string.Concat(
            ProductKeyPrefix,
            slug.Value);
    }

    public static string CreateSlugTag(
        ProductSlug slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return string.Concat(
            ProductSlugTagPrefix,
            slug.Value);
    }
}
