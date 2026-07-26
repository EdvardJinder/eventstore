using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Default event implementation materialized by EventStoreCore.
/// </summary>
public class Event : IEvent
{
    internal Event(DbEvent dbEvent)
        : this(dbEvent, EventTypeResolver.ResolveEventType(dbEvent, null))
    {
    }

    internal Event(DbEvent dbEvent, Type eventType)
        : this(dbEvent, eventType, Deserialize(
            dbEvent,
            eventType,
            new SystemTextJsonEventStoreSerializer()))
    {
    }

    internal Event(DbEvent dbEvent, Type eventType, object data)
    {
        ArgumentNullException.ThrowIfNull(dbEvent);
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(data);
        Id = dbEvent.EventId;
        StreamId = dbEvent.StreamId;
        Version = dbEvent.Version;
        Timestamp = dbEvent.Timestamp;
        TenantId = dbEvent.TenantId;
        EventType = eventType;
        Data = data;
        TypeName = dbEvent.TypeName;
        StreamType = dbEvent.StreamType;
        Sequence = dbEvent.Sequence;
        Metadata = new EventMetadata(
            dbEvent.CorrelationId,
            dbEvent.CausationId,
            dbEvent.Actor,
            EventHeaders.Deserialize(dbEvent.Headers),
            dbEvent.SchemaVersion <= 0 ? 1 : dbEvent.SchemaVersion,
            dbEvent.TypeName,
            dbEvent.StreamType,
            dbEvent.TenantId,
            dbEvent.StreamId,
            dbEvent.Version,
            dbEvent.Sequence);
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

    /// <inheritdoc />
    public string TypeName { get; }

    /// <inheritdoc />
    public string StreamType { get; }

    /// <inheritdoc />
    public long Sequence { get; }

    /// <inheritdoc />
    public EventMetadata Metadata { get; }

    internal static object Deserialize(
        DbEvent dbEvent,
        Type eventType,
        IEventStoreSerializer serializer,
        string? serializedData = null)
    {
        try
        {
            var data = serializer.Deserialize(serializedData ?? dbEvent.Data, eventType);
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
/// Strongly-typed event implementation materialized by EventStoreCore.
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public class Event<T> : Event, IEvent<T> where T : class
{
    internal Event(DbEvent dbEvent) : base(dbEvent)
    {
        Data = CastData(dbEvent, base.Data);
    }

    internal Event(DbEvent dbEvent, Type eventType) : base(dbEvent, eventType)
    {
        Data = CastData(dbEvent, base.Data);
    }

    internal Event(DbEvent dbEvent, Type eventType, object data) : base(dbEvent, eventType, data)
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

