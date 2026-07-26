using System.Collections.ObjectModel;

namespace EventStoreCore.Abstractions;

/// <summary>
/// Immutable metadata associated with an event.
/// </summary>
/// <remarks>
/// Correlation, causation, actor, and application headers may be supplied when an event is appended.
/// Ordering fields are assigned by the event store. Event identity may be caller-supplied through
/// <see cref="EventToAppend" />; persisted values are authoritative when the event is read.
/// </remarks>
public sealed class EventMetadata
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>
    /// Creates application-supplied event metadata.
    /// </summary>
    /// <param name="correlationId">The identifier shared by events in the same logical operation.</param>
    /// <param name="causationId">The identifier of the message or event that caused this event.</param>
    /// <param name="actor">The application-defined identity of the actor that produced the event.</param>
    /// <param name="headers">Application-defined headers. Reserved propagation names should not be used.</param>
    public EventMetadata(
        Guid? correlationId = null,
        Guid? causationId = null,
        string? actor = null,
        IReadOnlyDictionary<string, string>? headers = null)
        : this(
            correlationId,
            causationId,
            actor,
            headers,
            schemaVersion: 1,
            eventType: string.Empty,
            streamType: string.Empty,
            tenantId: Guid.Empty,
            streamId: Guid.Empty,
            streamVersion: 0,
            globalSequence: 0)
    {
    }

    internal EventMetadata(
        Guid? correlationId,
        Guid? causationId,
        string? actor,
        IReadOnlyDictionary<string, string>? headers,
        int schemaVersion,
        string eventType,
        string streamType,
        Guid tenantId,
        Guid streamId,
        long streamVersion,
        long globalSequence)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be greater than zero.");
        }

        CorrelationId = correlationId;
        CausationId = causationId;
        Actor = actor;
        Headers = headers is null || headers.Count == 0
            ? EmptyHeaders
            : new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));
        SchemaVersion = schemaVersion;
        EventType = eventType;
        StreamType = streamType;
        TenantId = tenantId;
        StreamId = streamId;
        StreamVersion = streamVersion;
        GlobalSequence = globalSequence;
    }

    /// <summary>
    /// The identifier shared by events in the same logical operation.
    /// </summary>
    public Guid? CorrelationId { get; }

    /// <summary>
    /// The identifier of the message or event that caused this event.
    /// </summary>
    public Guid? CausationId { get; }

    /// <summary>
    /// The application-defined identity of the actor that produced the event.
    /// </summary>
    public string? Actor { get; }

    /// <summary>
    /// Application-defined headers. The returned dictionary cannot be mutated.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// The persisted payload schema version.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// The logical event type name.
    /// </summary>
    public string EventType { get; }

    /// <summary>
    /// The logical stream type.
    /// </summary>
    public string StreamType { get; }

    /// <summary>
    /// The tenant identifier.
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// The event's version within its stream.
    /// </summary>
    public long StreamVersion { get; }

    /// <summary>
    /// The event's global ordering sequence.
    /// </summary>
    public long GlobalSequence { get; }
}
