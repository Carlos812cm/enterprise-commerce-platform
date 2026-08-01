using Catalog.Application.Products.GetPublishedProductBySlug;

namespace Catalog.Api.Endpoints.Storefront.GetPublishedProductBySlug;

public sealed record PublishedProductHttpResponse(
    Guid ProductId,
    string Name,
    string Slug,
    string Description,
    PublishedProductOptionHttpResponse[] Options,
    PublishedProductVariantHttpResponse[] Variants)
{
    public static PublishedProductHttpResponse From(
        PublishedProductDetailsReadModel product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new PublishedProductHttpResponse(
            product.ProductId,
            product.Name,
            product.Slug,
            product.Description,
            product.Options
                .Select(
                    static option =>
                        new PublishedProductOptionHttpResponse(
                            option.OptionId,
                            option.Name,
                            option.DisplayOrder))
                .ToArray(),
            product.Variants
                .Select(
                    static variant =>
                        new PublishedProductVariantHttpResponse(
                            variant.VariantId,
                            variant.Sku,
                            variant.Selections
                                .Select(
                                    static selection =>
                                        new PublishedProductVariantSelectionHttpResponse(
                                            selection.OptionId,
                                            selection.OptionName,
                                            selection.DisplayOrder,
                                            selection.Value))
                                .ToArray()))
                .ToArray());
    }
}

public sealed record PublishedProductOptionHttpResponse(
    Guid OptionId,
    string Name,
    int DisplayOrder);

public sealed record PublishedProductVariantHttpResponse(
    Guid VariantId,
    string Sku,
    PublishedProductVariantSelectionHttpResponse[]
        Selections);

public sealed record
    PublishedProductVariantSelectionHttpResponse(
        Guid OptionId,
        string OptionName,
        int DisplayOrder,
        string Value);
