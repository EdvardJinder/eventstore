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
    {
        Id = dbEvent.EventId;
        StreamId = dbEvent.StreamId;
        Version = dbEvent.Version;
        Timestamp = dbEvent.Timestamp;
        TenantId = dbEvent.TenantId;
        EventType = Type.GetType(dbEvent.Type!)!;
        Data = System.Text.Json.JsonSerializer.Deserialize(dbEvent.Data, EventType)!;
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
        Data = System.Text.Json.JsonSerializer.Deserialize<T>(dbEvent.Data)!;
    }

    /// <summary>
    /// The event payload.
    /// </summary>
    public new T Data { get; }
}

