using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Default event implementation backed by a <see cref="DbEvent" /> record.
/// </summary>
public class Event : IEvent
{
    /// <summary>
    /// Creates an event wrapper for a persisted event.
    /// </summary>
    /// <param name="dbEvent">The database event record.</param>
    public Event(DbEvent dbEvent)
        : this(dbEvent, EventTypeResolver.ResolveEventType(dbEvent, null))
    {
    }

    /// <summary>
    /// Creates an event wrapper for a persisted event using a resolved CLR type.
    /// </summary>
    /// <param name="dbEvent">The database event record.</param>
    /// <param name="eventType">The resolved CLR type.</param>
    public Event(DbEvent dbEvent, Type eventType)
    {
        ArgumentNullException.ThrowIfNull(dbEvent);
        ArgumentNullException.ThrowIfNull(eventType);
        Id = dbEvent.EventId;
        StreamId = dbEvent.StreamId;
        Version = dbEvent.Version;
        Timestamp = dbEvent.Timestamp;
        TenantId = dbEvent.TenantId;
        EventType = eventType;
        Data = Deserialize(dbEvent, eventType);
    }

    /// <summary>
    /// The event identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// The event version within the stream.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// The event payload.
    /// </summary>
    public object Data { get; }

    /// <summary>
    /// When the event was recorded in UTC.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// The tenant identifier for multi-tenant scenarios.
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// The CLR type of the event payload.
    /// </summary>
    public Type EventType { get; }

    private static object Deserialize(DbEvent dbEvent, Type eventType)
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize(dbEvent.Data, eventType);
            if (data is null)
            {
                throw new EventMaterializationException(
                    $"Could not deserialize event data to type '{eventType.FullName ?? eventType.Name}'.",
                    dbEvent);
            }

            return data;
        }
        catch (EventMaterializationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EventMaterializationException(
                $"Could not deserialize event data to type '{eventType.FullName ?? eventType.Name}'.",
                dbEvent,
                ex);
        }
    }
}

/// <summary>
/// Strongly-typed event implementation backed by a <see cref="DbEvent" /> record.
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public class Event<T> : Event, IEvent<T> where T : class
{
    /// <summary>
    /// Creates a typed event wrapper for a persisted event.
    /// </summary>
    /// <param name="dbEvent">The database event record.</param>
    public Event(DbEvent dbEvent) : base(dbEvent)
    {
        Data = CastData(dbEvent, base.Data);
    }

    /// <summary>
    /// Creates a typed event wrapper using a resolved CLR type.
    /// </summary>
    /// <param name="dbEvent">The database event record.</param>
    /// <param name="eventType">The resolved CLR type.</param>
    public Event(DbEvent dbEvent, Type eventType) : base(dbEvent, eventType)
    {
        Data = CastData(dbEvent, base.Data);
    }

    /// <summary>
    /// The event payload.
    /// </summary>
    public new T Data { get; }

    private static T CastData(DbEvent dbEvent, object data)
    {
        if (data is T typed)
        {
            return typed;
        }

        throw new EventMaterializationException(
            $"Could not deserialize event data to type '{typeof(T).FullName ?? typeof(T).Name}'.",
            dbEvent);
    }
}

