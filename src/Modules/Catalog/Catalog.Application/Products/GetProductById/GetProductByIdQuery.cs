using Commerce.Application.Messaging;

namespace Catalog.Application.Products.GetProductById;

public sealed record GetProductByIdQuery(
    Guid ProductId)
    : Query<AdminProductDetailsReadModel>;
