using Catalog.Domain.Products;

namespace Catalog.Application.Products.CreateDraftProduct;

public sealed record CreateDraftProductResponse(
    ProductId ProductId,
    ProductStatus Status);
