using EventStoreCore.Abstractions;

namespace EventStoreCore;

internal sealed class DbStream
{
    /// <summary>
    /// The current stream lifecycle state.
    /// </summary>
    public StreamLifecycleState LifecycleState { get; set; }

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

    /// <summary>
    /// The immutable lifecycle audit entries associated with this stream.
    /// </summary>
    public ICollection<DbStreamLifecycleEntry> LifecycleEntries { get; set; } = new List<DbStreamLifecycleEntry>();
}
