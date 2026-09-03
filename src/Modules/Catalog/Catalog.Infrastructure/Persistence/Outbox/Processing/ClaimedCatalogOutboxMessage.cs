namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed record ClaimedCatalogOutboxMessage(
    Guid Id,
    string MessageType,
    string Payload,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset EnqueuedAtUtc,
    int AttemptCount,
    string LeaseOwner,
    DateTimeOffset LockedUntilUtc,
    string? TraceParent,
    string? TraceState);
