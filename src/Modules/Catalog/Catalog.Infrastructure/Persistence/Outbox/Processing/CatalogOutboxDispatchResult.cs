namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed record CatalogOutboxDispatchResult
{
    private CatalogOutboxDispatchResult(
        CatalogOutboxDispatchOutcome outcome,
        string? errorCode)
    {
        Outcome = outcome;
        ErrorCode = errorCode;
    }

    public CatalogOutboxDispatchOutcome Outcome { get; }

    public string? ErrorCode { get; }

    public bool Succeeded =>
        Outcome == CatalogOutboxDispatchOutcome.Success;

    public static CatalogOutboxDispatchResult Success { get; } =
        new(
            CatalogOutboxDispatchOutcome.Success,
            null);

    public static CatalogOutboxDispatchResult TransientFailure(
        string errorCode)
    {
        return Failure(
            CatalogOutboxDispatchOutcome.TransientFailure,
            errorCode);
    }

    public static CatalogOutboxDispatchResult PermanentFailure(
        string errorCode)
    {
        return Failure(
            CatalogOutboxDispatchOutcome.PermanentFailure,
            errorCode);
    }

    private static CatalogOutboxDispatchResult Failure(
        CatalogOutboxDispatchOutcome outcome,
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorCode);

        return new CatalogOutboxDispatchResult(
            outcome,
            errorCode);
    }
}
