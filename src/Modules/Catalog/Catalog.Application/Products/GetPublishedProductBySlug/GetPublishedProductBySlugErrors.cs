using Commerce.Domain;

namespace Catalog.Application.Products.GetPublishedProductBySlug;

internal static class GetPublishedProductBySlugErrors
{
    public static DomainError NotFound { get; } =
        DomainError.NotFound(
            "Catalog.Storefront.ProductNotFound",
            "The requested storefront product does not exist.");
}
