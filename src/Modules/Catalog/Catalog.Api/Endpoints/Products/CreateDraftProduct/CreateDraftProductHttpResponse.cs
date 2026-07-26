namespace Catalog.Api.Endpoints.Products.CreateDraftProduct;

public sealed record CreateDraftProductHttpResponse(
    Guid ProductId,
    string Status);
