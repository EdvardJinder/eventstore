namespace EventStoreCore.Abstractions;

/// <summary>
/// Configures a bounded global event-log read.
/// </summary>
public sealed class EventLogReadOptions
{
    /// <summary>
    /// The exclusive lower global-sequence bound. Defaults to zero.
    /// </summary>
    public long AfterSequence { get; set; }

    /// <summary>
    /// The inclusive upper global-sequence bound. When omitted, the reader captures
    /// the highest currently visible global sequence before querying.
    /// </summary>
    public long? ThroughSequence { get; set; }

    /// <summary>
    /// The maximum number of events returned by one page. Defaults to 100.
    /// </summary>
    public int MaxCount { get; set; } = 100;

    /// <summary>
    /// Limits the read to one tenant. When omitted, events from every tenant are included.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Limits the read to the specified logical stream types. Null or an empty collection includes every stream type.
    /// </summary>
    public IReadOnlyCollection<string>? StreamTypes { get; set; }

    /// <summary>
    /// Limits the read to the specified logical event types. Null or an empty collection includes every event type.
    /// </summary>
    public IReadOnlyCollection<string>? EventTypes { get; set; }
}
