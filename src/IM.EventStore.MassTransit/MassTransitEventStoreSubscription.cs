using MassTransit;
using MassTransit.Configuration;
using Microsoft.Extensions.Options;

namespace IM.EventStore.MassTransit;

internal class MassTransitEventStoreSubscription(
    IBus bus,
    IOptions<MassTransitEventStoreSubscriptionOptions> options
    ) : ISubscription
{
    public async Task OnEventAsync(IEvent @event, CancellationToken cancellationToken)
    {
        var handler = options.Value.Handlers.FirstOrDefault(h => h.InEvent == @event.EventType);
        if (handler.OutEvent is null)
        {
            return;
        }
        var transformed = handler.Transform((IEvent<object>)@event);
        await bus.Publish(transformed, handler.OutEvent, cancellationToken);
    }
}

