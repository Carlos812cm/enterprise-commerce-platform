using Commerce.Domain;

namespace Catalog.Application.Products.PublishProduct;

internal static class PublishProductErrors
{
    public static DomainError InvalidProductId { get; } =
        DomainError.Validation(
            "Catalog.Product.InvalidId",
            "The product identifier cannot be empty.");

    public static DomainError NotFound { get; } =
        DomainError.NotFound(
            "Catalog.Product.NotFound",
            "The requested product does not exist.");

    public static DomainError ConcurrencyConflict { get; } =
        DomainError.Conflict(
            "Catalog.Product.ConcurrencyConflict",
            "The product changed while publication was being processed. Reload it and retry.");
}
