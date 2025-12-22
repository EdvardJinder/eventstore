

namespace EJ.EventStore.Abstractions;



public interface ISubscription
{
    Task Handle(IEvent @event, CancellationToken ct);
}

