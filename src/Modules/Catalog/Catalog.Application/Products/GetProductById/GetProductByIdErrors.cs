using Commerce.Domain;

namespace Catalog.Application.Products.GetProductById;

internal static class GetProductByIdErrors
{
    public static DomainError InvalidProductId { get; } =
        DomainError.Validation(
            "Catalog.Product.InvalidId",
            "The product identifier cannot be empty.");

    public static DomainError NotFound { get; } =
        DomainError.NotFound(
            "Catalog.Product.NotFound",
            "The requested product does not exist.");
}
