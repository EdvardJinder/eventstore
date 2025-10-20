using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.MassTransit;

internal class MassTransitSubscription : ISubscription
{
    public static async Task Handle(IEvent @event, IServiceProvider sp, CancellationToken ct)
    {
        var scope = sp.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var eventType = typeof(EventContext<>).MakeGenericType(@event.EventType);
        var eventContext = Activator.CreateInstance(eventType)!;

        eventType.GetProperty(nameof(EventContext<object>.Data))?.SetValue(eventContext, @event.Data);
        eventType.GetProperty(nameof(EventContext<object>.EventId))?.SetValue(eventContext, @event.Id);
        eventType.GetProperty(nameof(EventContext<object>.StreamId))?.SetValue(eventContext, @event.StreamId);
        eventType.GetProperty(nameof(EventContext<object>.Version))?.SetValue(eventContext, @event.Version);
        eventType.GetProperty(nameof(EventContext<object>.Timestamp))?.SetValue(eventContext, @event.Timestamp);
        eventType.GetProperty(nameof(EventContext<object>.TenantId))?.SetValue(eventContext, @event.TenantId);

        await bus.Publish(eventContext, ct);
    }

   

}
