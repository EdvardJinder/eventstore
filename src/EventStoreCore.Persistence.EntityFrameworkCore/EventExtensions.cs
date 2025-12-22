using EventStoreCore.Abstractions;

namespace EventStoreCore;

public static class EventExtensions
{
    public static IEvent ToEvent(this DbEvent dbEvent)
    {
        var eventType = Type.GetType(dbEvent.Type);
        if (eventType == null)
            throw new InvalidOperationException($"Could not load event type {dbEvent.Type}");
        var data = System.Text.Json.JsonSerializer.Deserialize(dbEvent.Data, eventType);
        if (data == null)
            throw new InvalidOperationException($"Could not deserialize event data to type {dbEvent.Type}");
        var eventInstanceType = typeof(Event<>).MakeGenericType(eventType);
        var eventInstance = Activator.CreateInstance(eventInstanceType, dbEvent);

        if (eventInstance == null)
            throw new InvalidOperationException($"Could not create instance of event type {eventInstanceType}");

        return (IEvent)eventInstance;
    }
}
