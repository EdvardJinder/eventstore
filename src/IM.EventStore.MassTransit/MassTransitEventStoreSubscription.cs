using MassTransit;
using System.Text.Json.Serialization;

namespace IM.EventStore.MassTransit;

internal class MassTransitEventStoreSubscription(
    IBus bus
    ) : ISubscription
{
    public async Task HandleBatchAsync(IEvent[] events, CancellationToken ct)
    {
       foreach(var @event in events)
        {
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

}


public class EventContext<T> where T : class
{
    [JsonInclude] public T Data { get; private set; }
    [JsonInclude] public Guid EventId { get; private set; }
    [JsonInclude] public Guid StreamId { get; private set; }
    [JsonInclude] public long Version { get; private set; }
    [JsonInclude] public DateTimeOffset Timestamp { get; private set; }
    [JsonInclude] public Guid TenantId { get; private set; }

}