using EventStoreCore.Abstractions;

namespace EventStoreCore;

internal sealed class SubscriptionRegistration
{
    public required string Name { get; init; }
    public required ISubscription Subscription { get; init; }
    public required SubscriptionRegistrationOptions Options { get; init; }
}

internal sealed class TypedSubscriptionAdapter<TSubscription, TEvent>(TSubscription subscription) : ISubscription
    where TSubscription : class, ISubscription<TEvent>
    where TEvent : class
{
    public Task Handle(IEvent @event, CancellationToken ct) =>
        subscription.Handle(new TypedEvent<TEvent>(@event), ct);
}

internal sealed class TypedEvent<TEvent>(IEvent source) : IEvent<TEvent>
    where TEvent : class
{
    public Guid Id => source.Id;
    public long Version => source.Version;
    public TEvent Data => (TEvent)source.Data;
    object IEvent.Data => Data;
    public Guid StreamId => source.StreamId;
    public DateTimeOffset Timestamp => source.Timestamp;
    public Guid TenantId => source.TenantId;
    public Type EventType => source.EventType;
}
