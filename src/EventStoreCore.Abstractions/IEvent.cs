
namespace EventStoreCore.Abstractions;

/// <summary>
/// Defines the contract for an event within an event stream, providing metadata and payload information required
/// for event sourcing and processing.
/// </summary>
/// <remarks>Implementations of this interface represent discrete events in a stream, including
/// identifiers, versioning, timestamps, and tenant context for multi-tenant scenarios. The interface is designed to
/// support event sourcing patterns, allowing consumers to track, process, and reconstruct state from event streams.
/// All properties are read-only and provide essential information for event handling and auditing.</remarks>
public interface IEvent
{
    /// <summary>
    ///     Unique identifier for the event. Uses a sequential Guid
    /// </summary>
    Guid Id { get; }

    /// <summary>
    ///     The version of the stream this event reflects. The place in the stream.
    /// </summary>
    long Version { get; }


    /// <summary>
    ///     The actual event data body
    /// </summary>
    object Data { get; }

    /// <summary>
    ///     Stream's Id
    /// </summary>
    Guid StreamId { get; }

    /// <summary>
    ///     The UTC time that this event was originally captured
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     If using multi-tenancy by tenant id
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    ///     The .Net type of the event body
    /// </summary>
    Type EventType { get; }


}

public interface IEvent<out T> : IEvent where T : class
{
    /// <summary>
    ///     The actual event data body
    /// </summary>
    new T Data { get; }
}