namespace Catalog.Application.Products.GetPublishedProductBySlug;

public sealed record PublishedProductDetailsReadModel(
    Guid ProductId,
    string Name,
    string Slug,
    string Description,
    long Version,
    PublishedProductOptionReadModel[] Options,
    PublishedProductVariantReadModel[] Variants);

public sealed record PublishedProductOptionReadModel(
    Guid OptionId,
    string Name,
    int DisplayOrder);

public sealed record PublishedProductVariantReadModel(
    Guid VariantId,
    string Sku,
    PublishedProductVariantSelectionReadModel[] Selections);

public sealed record PublishedProductVariantSelectionReadModel(
    Guid OptionId,
    string OptionName,
    int DisplayOrder,
    string Value);
