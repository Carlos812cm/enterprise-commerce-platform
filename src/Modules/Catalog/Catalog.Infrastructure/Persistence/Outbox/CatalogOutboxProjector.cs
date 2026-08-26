using System.Diagnostics;
using System.Text.Json;
using Catalog.Contracts.Products;
using Catalog.Domain.Products.Events;
using Catalog.Infrastructure.Persistence.Records;
using Commerce.Domain;

namespace Catalog.Infrastructure.Persistence.Outbox;

internal static class CatalogOutboxProjector
{
    private const int TraceParentMaximumLength = 55;
    private const int TraceStateMaximumLength = 512;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static OutboxMessageRecord[] Project(
        IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            ProductPublishedDomainEvent productPublished =>
                ProjectProductPublished(
                    productPublished),

            _ => []
        };
    }

    private static OutboxMessageRecord[] ProjectProductPublished(
        ProductPublishedDomainEvent domainEvent)
    {
        var cacheInvalidation =
            new StorefrontProductCacheInvalidationV1(
                domainEvent.ProductId.Value,
                domainEvent.Slug.Value,
                domainEvent.OccurredAtUtc);

        var integrationEvent =
            new ProductPublishedIntegrationEventV1(
                domainEvent.ProductId.Value,
                domainEvent.Slug.Value,
                domainEvent.OccurredAtUtc);

        return
        [
            CreateMessage(
                CatalogOutboxMessageTypes
                    .StorefrontProductCacheInvalidateV1,
                cacheInvalidation,
                domainEvent.OccurredAtUtc),

            CreateMessage(
                CatalogOutboxMessageTypes
                    .ProductPublishedV1,
                integrationEvent,
                domainEvent.OccurredAtUtc)
        ];
    }

    private static OutboxMessageRecord CreateMessage<TPayload>(
        string messageType,
        TPayload payload,
        DateTimeOffset occurredAtUtc)
    {
        var activity = Activity.Current;

        string? traceParent = null;
        string? traceState = null;

        if (activity is
            {
                IdFormat: ActivityIdFormat.W3C
            })
        {
            if (activity.Id is { } activityId &&
                activityId.Length <=
                TraceParentMaximumLength)
            {
                traceParent = activityId;
            }

            if (activity.TraceStateString is { } activityTraceState &&
                activityTraceState.Length <=
                TraceStateMaximumLength)
            {
                traceState = activityTraceState;
            }
        }

        return new OutboxMessageRecord
        {
            Id = Guid.CreateVersion7(),
            MessageType = messageType,
            Payload = JsonSerializer.Serialize(
                payload,
                SerializerOptions),
            OccurredAtUtc = occurredAtUtc,
            TraceParent = traceParent,
            TraceState = traceState
        };
    }
}
