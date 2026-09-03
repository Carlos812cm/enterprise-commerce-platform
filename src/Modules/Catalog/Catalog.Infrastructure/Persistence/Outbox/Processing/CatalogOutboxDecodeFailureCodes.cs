namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal static class CatalogOutboxDecodeFailureCodes
{
    public const string UnsupportedMessageType =
        "catalog.outbox.unsupported-message-type";

    public const string InvalidPayload =
        "catalog.outbox.invalid-payload";
}
