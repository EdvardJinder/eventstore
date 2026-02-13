namespace EventStoreCore;

/// <summary>
/// Represents a persisted event stream and its metadata.
/// </summary>
public sealed class DbStream
{
    /// <summary>
    /// The tenant identifier for multi-tenant scenarios.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The stream type for distinguishing multiple streams with the same ID.
    /// </summary>
    public string StreamType { get; set; } = string.Empty;

    /// <summary>
    /// The current version of the stream.
    /// </summary>
    public long CurrentVersion { get; set; }

    /// <summary>
    /// When the stream was created in UTC.
    /// </summary>
    public DateTimeOffset CreatedTimestamp { get; set; }

    /// <summary>
    /// When the stream was last updated in UTC.
    /// </summary>
    public DateTimeOffset UpdatedTimestamp { get; set; }

    /// <summary>
    /// The events associated with this stream.
    /// </summary>
    public ICollection<DbEvent> Events { get; set; } = new List<DbEvent>();
}

