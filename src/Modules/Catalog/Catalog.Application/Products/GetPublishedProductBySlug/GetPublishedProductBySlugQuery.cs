using Commerce.Application.Messaging;

namespace Catalog.Application.Products.GetPublishedProductBySlug;

public sealed record GetPublishedProductBySlugQuery(
    string? Slug)
    : Query<PublishedProductDetailsReadModel>;
