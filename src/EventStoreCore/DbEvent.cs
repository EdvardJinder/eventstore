namespace EventStoreCore;

/// <summary>
/// Represents a persisted event record.
/// </summary>
public sealed class DbEvent
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
    /// The event version within the stream.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// The global sequence number for ordering across streams.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The assembly-qualified name of the event payload type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

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
}

