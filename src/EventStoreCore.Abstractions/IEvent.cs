
namespace EventStoreCore.Abstractions;

/// <summary>
/// Defines the contract for an event within an event stream, providing metadata and payload information required
/// for event sourcing and processing.
/// </summary>
/// <remarks>Implementations of this interface represent discrete events in a stream, including
/// identifiers, versioning, timestamps, and tenant context for multi-tenant scenarios. The interface is designed to
/// support event sourcing patterns, allowing consumers to track, process, and reconstruct state from event streams.
/// All properties are read-only and provide essential information for event handling and auditing.</remarks>
public interface IEvent : IEventEnvelope
{
    /// <summary>
    ///     Unique stable identifier for the event. The identifier is not an ordering value.
    /// </summary>
    new Guid Id { get; }

    /// <summary>
    ///     The version of the stream this event reflects. The place in the stream.
    /// </summary>
    long Version { get; }


    /// <summary>
    ///     The actual event data body
    /// </summary>
    new object Data { get; }

    /// <summary>
    ///     Stream's Id
    /// </summary>
    Guid StreamId { get; }

    /// <summary>
    ///     The UTC time that this event was originally captured
    /// </summary>
    new DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     If using multi-tenancy by tenant id
    /// </summary>
    new Guid TenantId { get; }

    /// <summary>
    ///     The .Net type of the event body
    /// </summary>
    new Type EventType { get; }

    Guid IEventEnvelope.Id => Id;

    object IEventEnvelope.Data => Data;

    Type IEventEnvelope.EventType => EventType;

    DateTimeOffset IEventEnvelope.Timestamp => Timestamp;

    Guid IEventEnvelope.TenantId => TenantId;

    /// <summary>
    ///     The logical event type name stored independently of the CLR type name.
    /// </summary>
    string TypeName => EventType.Name;

    /// <summary>
    ///     The logical stream type. The empty string identifies the default stream type.
    /// </summary>
    string StreamType => string.Empty;

    /// <summary>
    ///     The global ordering sequence assigned by persistence.
    /// </summary>
    long Sequence => 0;

    /// <summary>
    ///     Immutable event metadata, including application context and authoritative ordering fields.
    /// </summary>
    EventMetadata Metadata => new(
        correlationId: null,
        causationId: null,
        actor: null,
        headers: null,
        schemaVersion: 1,
        eventType: TypeName,
        streamType: StreamType,
        tenantId: TenantId,
        streamId: StreamId,
        streamVersion: Version,
        globalSequence: Sequence);

}

/// <summary>
/// Defines a strongly-typed event wrapper.
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public interface IEvent<out T> : IEvent, IEventEnvelope<T> where T : class
{
    /// <summary>
    /// The event payload.
    /// </summary>
    new T Data { get; }

    object IEventEnvelope.Data => Data;
}
