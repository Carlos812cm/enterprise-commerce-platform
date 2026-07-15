using Commerce.Domain;
using Xunit;

namespace Commerce.Domain.UnitTests;

public sealed class AggregateRootTests
{
    [Fact]
    public void RaisedDomainEventIsExposed()
    {
        var aggregate = new TestAggregate(
            Guid.CreateVersion7());

        var domainEvent = new TestDomainEvent(
            new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero));

        aggregate.Record(domainEvent);

        var recordedEvent = Assert.Single(
            aggregate.DomainEvents);

        Assert.Same(domainEvent, recordedEvent);
    }

    [Fact]
    public void DequeueReturnsEventsAndClearsCollection()
    {
        var aggregate = new TestAggregate(
            Guid.CreateVersion7());

        var domainEvent = new TestDomainEvent(
            new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero));

        aggregate.Record(domainEvent);

        var dequeuedEvents =
            aggregate.DequeueDomainEvents();

        var dequeuedEvent = Assert.Single(
            dequeuedEvents);

        Assert.Same(domainEvent, dequeuedEvent);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void RecordingNullDomainEventThrows()
    {
        var aggregate = new TestAggregate(
            Guid.CreateVersion7());

        Assert.Throws<ArgumentNullException>(() =>
        {
            aggregate.Record(null!);
        });
    }

    private sealed class TestAggregate(Guid id)
        : AggregateRoot<Guid>(id)
    {
        public void Record(IDomainEvent domainEvent)
        {
            RaiseDomainEvent(domainEvent);
        }
    }

    private sealed record TestDomainEvent(
        DateTimeOffset OccurredAtUtc) : IDomainEvent;
}
