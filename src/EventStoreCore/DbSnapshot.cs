namespace EventStoreCore;

internal sealed class DbSnapshot
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
    /// The stable CLR name of the state type stored in the snapshot.
    /// </summary>
    public required string StateType { get; set; }

    /// <summary>
    /// The stream version represented by the snapshot.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// The serialized state payload.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// When the snapshot was recorded in UTC.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The serialized snapshot schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
