namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed record CatalogOutboxFailureResult(
    bool Updated,
    bool DeadLettered,
    int? AttemptCount,
    DateTimeOffset? NextAttemptAtUtc)
{
    public static CatalogOutboxFailureResult LeaseLost { get; } =
        new(
            false,
            false,
            null,
            null);
}
