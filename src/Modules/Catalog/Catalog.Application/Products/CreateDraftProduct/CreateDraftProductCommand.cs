using Commerce.Application.Messaging;

namespace Catalog.Application.Products.CreateDraftProduct;

public sealed record CreateDraftProductCommand(
    string? Name,
    string? Slug,
    string? Description)
    : Command<CreateDraftProductResponse>;
