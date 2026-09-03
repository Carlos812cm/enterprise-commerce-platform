namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed record CatalogOutboxProcessResult(
    CatalogOutboxProcessOutcome Outcome,
    string? ErrorCode,
    int? AttemptCount,
    DateTimeOffset? NextAttemptAtUtc)
{
    public static CatalogOutboxProcessResult Processed { get; } =
        new(
            CatalogOutboxProcessOutcome.Processed,
            null,
            null,
            null);

    public static CatalogOutboxProcessResult LeaseLost { get; } =
        new(
            CatalogOutboxProcessOutcome.LeaseLost,
            null,
            null,
            null);

    public static CatalogOutboxProcessResult RetryScheduled(
        string errorCode,
        int attemptCount,
        DateTimeOffset nextAttemptAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorCode);

        return new CatalogOutboxProcessResult(
            CatalogOutboxProcessOutcome.RetryScheduled,
            errorCode,
            attemptCount,
            nextAttemptAtUtc);
    }

    public static CatalogOutboxProcessResult DeadLettered(
        string errorCode,
        int attemptCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorCode);

        return new CatalogOutboxProcessResult(
            CatalogOutboxProcessOutcome.DeadLettered,
            errorCode,
            attemptCount,
            null);
    }
}
