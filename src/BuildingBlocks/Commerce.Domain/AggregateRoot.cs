using System.Collections.ObjectModel;

namespace Commerce.Domain;

public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly ReadOnlyCollection<IDomainEvent> _readOnlyDomainEvents;

    protected AggregateRoot(TId id)
        : base(id)
    {
        _readOnlyDomainEvents = _domainEvents.AsReadOnly();
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents =>
        _readOnlyDomainEvents;

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _domainEvents.Add(domainEvent);
    }

    public IDomainEvent[] DequeueDomainEvents()
    {
        var domainEvents = _domainEvents.ToArray();

        _domainEvents.Clear();

        return domainEvents;
    }
}
