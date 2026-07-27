using Catalog.Application.Products.GetProductById;

namespace Catalog.Api.Endpoints.Products.GetProductById;

public sealed record GetProductByIdHttpResponse(
    Guid ProductId,
    string Name,
    string Slug,
    string Description,
    string Status,
    long Version,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? DiscontinuedAtUtc,
    IReadOnlyList<GetProductOptionHttpResponse> Options,
    IReadOnlyList<GetProductVariantHttpResponse> Variants)
{
    public static GetProductByIdHttpResponse From(
        AdminProductDetailsReadModel product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new GetProductByIdHttpResponse(
            product.ProductId,
            product.Name,
            product.Slug,
            product.Description,
            product.Status,
            product.Version,
            product.PublishedAtUtc,
            product.DiscontinuedAtUtc,
            product.Options
                .Select(
                    static option =>
                        new GetProductOptionHttpResponse(
                            option.OptionId,
                            option.Name,
                            option.DisplayOrder))
                .ToArray(),
            product.Variants
                .Select(
                    static variant =>
                        new GetProductVariantHttpResponse(
                            variant.VariantId,
                            variant.Sku,
                            variant.Status,
                            variant.ActivatedAtUtc,
                            variant.DiscontinuedAtUtc,
                            variant.Selections
                                .Select(
                                    static selection =>
                                        new GetProductVariantSelectionHttpResponse(
                                            selection.OptionId,
                                            selection.OptionName,
                                            selection.DisplayOrder,
                                            selection.Value))
                                .ToArray()))
                .ToArray());
    }
}

public sealed record GetProductOptionHttpResponse(
    Guid OptionId,
    string Name,
    int DisplayOrder);

public sealed record GetProductVariantHttpResponse(
    Guid VariantId,
    string Sku,
    string Status,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? DiscontinuedAtUtc,
    IReadOnlyList<
        GetProductVariantSelectionHttpResponse> Selections);

public sealed record GetProductVariantSelectionHttpResponse(
    Guid OptionId,
    string OptionName,
    int DisplayOrder,
    string Value);
