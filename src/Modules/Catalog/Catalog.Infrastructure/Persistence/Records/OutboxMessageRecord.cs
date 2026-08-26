namespace Catalog.Infrastructure.Persistence.Records;

internal sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }

    public string MessageType { get; set; } =
        string.Empty;

    public string Payload { get; set; } =
        string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public DateTimeOffset EnqueuedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public string? LockOwner { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? TraceParent { get; set; }

    public string? TraceState { get; set; }
}
