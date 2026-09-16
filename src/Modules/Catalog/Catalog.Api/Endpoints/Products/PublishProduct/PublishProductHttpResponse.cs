namespace Catalog.Api.Endpoints.Products.PublishProduct;

public sealed record PublishProductHttpResponse(
    Guid ProductId,
    string Status,
    DateTimeOffset PublishedAtUtc);
