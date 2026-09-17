using Commerce.Application.Messaging;

namespace Catalog.Application.Products.PublishProduct;

public sealed record PublishProductCommand(
    Guid ProductId)
    : Command<PublishProductResponse>;
