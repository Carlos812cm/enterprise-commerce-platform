namespace Catalog.Infrastructure.Messaging.RabbitMq;

internal static class CatalogRabbitMqPublishFailureCodes
{
    public const string Unroutable =
        "catalog.rabbitmq.unroutable";

    public const string Nacked =
        "catalog.rabbitmq.nacked";

    public const string Unavailable =
        "catalog.rabbitmq.unavailable";

    public const string SerializationFailed =
        "catalog.rabbitmq.serialization-failed";
}
