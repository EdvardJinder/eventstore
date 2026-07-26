using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Represents an event captured atomically from an EF entity change.
/// </summary>
public sealed class DbOutboxMessage
{
    /// <summary>
    /// The ordered database-generated outbox position.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The unique event identifier.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// The tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The assembly-qualified payload type name.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// The logical payload type name.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// The serialized payload.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// When the event was captured in UTC.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The assembly-qualified source entity type name.
    /// </summary>
    public string SourceEntityType { get; set; } = string.Empty;

    /// <summary>
    /// A JSON object containing the source entity's primary-key values.
    /// </summary>
    public string SourceEntityKey { get; set; } = "{}";

    /// <summary>
    /// The entity change that produced the event.
    /// </summary>
    public EntityChangeKind ChangeKind { get; set; }
}
