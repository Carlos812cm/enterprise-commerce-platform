using Commerce.Domain;

namespace Catalog.Application.Products.CreateDraftProduct;

internal static class CreateDraftProductErrors
{
    public static DomainError SlugAlreadyExists { get; } =
        DomainError.Conflict(
            "Catalog.Product.SlugAlreadyExists",
            "A product with the same slug already exists.");
}
