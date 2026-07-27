namespace Catalog.Application.Products.GetProductById;

public sealed record AdminProductDetailsReadModel(
    Guid ProductId,
    string Name,
    string Slug,
    string Description,
    string Status,
    long Version,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? DiscontinuedAtUtc,
    IReadOnlyList<AdminProductOptionReadModel> Options,
    IReadOnlyList<AdminProductVariantReadModel> Variants);

public sealed record AdminProductOptionReadModel(
    Guid OptionId,
    string Name,
    int DisplayOrder);

public sealed record AdminProductVariantReadModel(
    Guid VariantId,
    string Sku,
    string Status,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? DiscontinuedAtUtc,
    IReadOnlyList<
        AdminProductVariantSelectionReadModel> Selections);

public sealed record AdminProductVariantSelectionReadModel(
    Guid OptionId,
    string OptionName,
    int DisplayOrder,
    string Value);
