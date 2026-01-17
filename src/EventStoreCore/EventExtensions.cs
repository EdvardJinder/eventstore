using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Extension helpers for translating persisted events.
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Converts a <see cref="DbEvent" /> record into a runtime <see cref="IEvent" /> instance.
    /// </summary>
    /// <param name="dbEvent">The persisted event record.</param>
    /// <returns>The deserialized event wrapper.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the event type or payload cannot be loaded.</exception>
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

