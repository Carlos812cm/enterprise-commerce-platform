namespace Catalog.Infrastructure.Messaging.RabbitMq;

internal static class CatalogRabbitMqTopology
{
    public const string IntegrationEventsExchange =
        "commerce.events";

    public const string ProductPublishedRoutingKey =
        "catalog.product-published.v1";

    public const string ContentType =
        "application/json";
}
