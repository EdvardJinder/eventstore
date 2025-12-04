
using IM.EventStore.Abstractions;

namespace IM.EventStore;



public interface ISubscription
{
    Task Handle(IEvent @event, CancellationToken ct);
}

