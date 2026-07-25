namespace Catalog.Api.Endpoints.Products.CreateDraftProduct;

public sealed record CreateDraftProductRequest(
    string? Name,
    string? Slug,
    string? Description);
