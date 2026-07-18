using Commerce.Domain;

namespace Catalog.Domain.Products.Events;

public sealed record ProductDraftCreatedDomainEvent(
    ProductId ProductId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ProductVariantAddedDomainEvent(
    ProductId ProductId,
    ProductVariantId ProductVariantId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ProductVariantActivatedDomainEvent(
    ProductId ProductId,
    ProductVariantId ProductVariantId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ProductVariantDiscontinuedDomainEvent(
    ProductId ProductId,
    ProductVariantId ProductVariantId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ProductPublishedDomainEvent(
    ProductId ProductId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ProductDiscontinuedDomainEvent(
    ProductId ProductId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
