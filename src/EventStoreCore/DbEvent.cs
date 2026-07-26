namespace EventStoreCore;

internal sealed class DbEvent
{
    /// <summary>
    /// The tenant identifier for multi-tenant scenarios.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid StreamId { get; set; }

    /// <summary>
    /// The stream type for distinguishing multiple streams with the same ID.
    /// </summary>
    public string StreamType { get; set; } = string.Empty;

    /// <summary>
    /// The event version within the stream.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// The global sequence number for ordering across streams.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// Required. The assembly-qualified name of the event payload type.
    /// </summary>
    public required string Type { get; set; } = string.Empty;

    /// <summary>
    /// The logical event type name used for compatibility across renamed namespaces.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// The serialized event payload.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// When the event was recorded in UTC.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The event identifier.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// The correlation identifier supplied by the application.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// The causation identifier supplied by the application.
    /// </summary>
    public Guid? CausationId { get; set; }

    /// <summary>
    /// The application-defined actor identity.
    /// </summary>
    public string? Actor { get; set; }

    /// <summary>
    /// Serialized application-defined headers.
    /// </summary>
    public string Headers { get; set; } = "{}";

    /// <summary>
    /// The serialized payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}

