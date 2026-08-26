namespace Catalog.Contracts.Products;

public sealed record ProductPublishedIntegrationEventV1(
    Guid ProductId,
    string Slug,
    DateTimeOffset PublishedAtUtc);
