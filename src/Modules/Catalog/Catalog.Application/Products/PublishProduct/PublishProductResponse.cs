using Catalog.Domain.Products;

namespace Catalog.Application.Products.PublishProduct;

public sealed record PublishProductResponse(
    ProductId ProductId,
    ProductStatus Status,
    DateTimeOffset PublishedAtUtc);
