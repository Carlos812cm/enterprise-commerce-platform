using Commerce.Domain;

namespace Catalog.Domain.Products;

internal static class ProductVariantErrors
{
    public static DomainError SkuIsImmutable { get; } =
        DomainError.Conflict(
            "Catalog.Variant.SkuIsImmutable",
            "The SKU cannot be changed after the variant has been activated.");

    public static DomainError OptionsAreImmutable { get; } =
        DomainError.Conflict(
            "Catalog.Variant.OptionsAreImmutable",
            "The option combination cannot be changed after the variant has been activated.");

    public static DomainError IsDiscontinued { get; } =
        DomainError.Conflict(
            "Catalog.Variant.IsDiscontinued",
            "A discontinued variant cannot be modified.");

    public static DomainError CannotActivateDiscontinued { get; } =
        DomainError.Conflict(
            "Catalog.Variant.CannotActivateDiscontinued",
            "A discontinued variant cannot be activated.");

    public static DomainError DiscontinuedBeforeActivation { get; } =
        DomainError.Validation(
            "Catalog.Variant.DiscontinuedBeforeActivation",
            "The discontinuation timestamp cannot be earlier than the activation timestamp.");
}
