using Commerce.Domain;

namespace Catalog.Domain.Products;

internal static class ProductErrors
{
    public static DomainError IsDiscontinued { get; } =
        DomainError.Conflict(
            "Catalog.Product.IsDiscontinued",
            "A discontinued product cannot be modified.");

    public static DomainError AlreadyPublished { get; } =
        DomainError.Conflict(
            "Catalog.Product.AlreadyPublished",
            "The product has already been published.");

    public static DomainError SlugIsImmutable { get; } =
        DomainError.Conflict(
            "Catalog.Product.SlugIsImmutable",
            "The product slug cannot be changed after publication.");

    public static DomainError OptionSchemaFrozen { get; } =
        DomainError.Conflict(
            "Catalog.Product.OptionSchemaFrozen",
            "The option schema cannot be changed after publication.");

    public static DomainError OptionSchemaLockedByVariants { get; } =
        DomainError.Conflict(
            "Catalog.Product.OptionSchemaLockedByVariants",
            "The option schema cannot be changed after variants have been added.");

    public static DomainError DuplicateOptionName { get; } =
        DomainError.Conflict(
            "Catalog.Product.DuplicateOptionName",
            "The product already contains an option with the same name.");

    public static DomainError DuplicateOptionDisplayOrder { get; } =
        DomainError.Conflict(
            "Catalog.Product.DuplicateOptionDisplayOrder",
            "The product already contains an option with the same display order.");

    public static DomainError OptionsNotDefined { get; } =
        DomainError.Validation(
            "Catalog.Variant.OptionsNotDefined",
            "A product without option definitions requires an empty option combination.");

    public static DomainError MissingOptionSelection { get; } =
        DomainError.Validation(
            "Catalog.Variant.MissingOptionSelection",
            "The variant must contain one selection for every product option.");

    public static DomainError UnknownOptionSelection { get; } =
        DomainError.Validation(
            "Catalog.Variant.UnknownOptionSelection",
            "The variant contains a selection for an option that is not defined by the product.");

    public static DomainError DuplicateSku { get; } =
        DomainError.Conflict(
            "Catalog.Variant.DuplicateSku",
            "The product already contains a variant with the same SKU.");

    public static DomainError DuplicateVariantCombination { get; } =
        DomainError.Conflict(
            "Catalog.Variant.DuplicateOptionCombination",
            "The product already contains a non-discontinued variant with the same option combination.");

    public static DomainError VariantNotFound { get; } =
        DomainError.NotFound(
            "Catalog.Variant.NotFound",
            "The requested product variant does not exist.");

    public static DomainError ProductMustBePublished { get; } =
        DomainError.Conflict(
            "Catalog.Product.MustBePublished",
            "The product must be published before a variant can be activated independently.");

    public static DomainError NoPublishableVariants { get; } =
        DomainError.Validation(
            "Catalog.Product.NoPublishableVariants",
            "The product must contain at least one draft variant before publication.");

    public static DomainError LastActiveVariant { get; } =
        DomainError.Conflict(
            "Catalog.Variant.LastActiveVariant",
            "The last active variant cannot be discontinued while the product remains published.");

    public static DomainError InvalidActivationTimestamp { get; } =
        DomainError.Validation(
            "Catalog.Variant.InvalidActivationTimestamp",
            "The variant activation timestamp cannot be earlier than the product publication timestamp.");

    public static DomainError InvalidDiscontinuationTimestamp { get; } =
        DomainError.Validation(
            "Catalog.Product.InvalidDiscontinuationTimestamp",
            "The discontinuation timestamp cannot be earlier than publication or variant activation.");
}
