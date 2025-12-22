

namespace EventStoreCore.Abstractions;



public interface ISubscription
{
    Task Handle(IEvent @event, CancellationToken ct);
}

