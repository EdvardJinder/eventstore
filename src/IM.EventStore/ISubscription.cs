
using IM.EventStore.Abstractions;

namespace IM.EventStore;



public interface ISubscription
{
    static abstract Task Handle(IEvent @event, IServiceProvider sp, CancellationToken ct);
}

