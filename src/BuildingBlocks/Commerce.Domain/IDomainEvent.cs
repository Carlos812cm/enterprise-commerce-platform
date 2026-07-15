namespace Commerce.Domain;

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}
