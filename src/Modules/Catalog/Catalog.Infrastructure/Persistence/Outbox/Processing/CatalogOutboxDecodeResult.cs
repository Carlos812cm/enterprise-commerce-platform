namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed class CatalogOutboxDecodeResult
{
    private CatalogOutboxDecodeResult(
        DecodedCatalogOutboxMessage? message,
        string? errorCode)
    {
        Message = message;
        ErrorCode = errorCode;
    }

    public DecodedCatalogOutboxMessage? Message { get; }

    public string? ErrorCode { get; }

    public bool Succeeded =>
        Message is not null;

    public static CatalogOutboxDecodeResult Success(
        DecodedCatalogOutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return new CatalogOutboxDecodeResult(
            message,
            null);
    }

    public static CatalogOutboxDecodeResult Failure(
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorCode);

        return new CatalogOutboxDecodeResult(
            null,
            errorCode);
    }
}
