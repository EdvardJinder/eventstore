namespace EventStoreCore;

/// <summary>
/// Contains persisted event data supplied to a custom unknown-event handler.
/// </summary>
/// <param name="EventId">The event identifier.</param>
/// <param name="StreamId">The stream identifier.</param>
/// <param name="StreamType">The logical stream type.</param>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="Sequence">The global sequence.</param>
/// <param name="Version">The stream version.</param>
/// <param name="LogicalTypeName">The persisted logical event type name.</param>
/// <param name="ClrTypeName">The persisted CLR type name.</param>
/// <param name="Data">The raw JSON payload.</param>
/// <param name="Timestamp">The persisted event timestamp.</param>
/// <param name="Exception">The materialization failure.</param>
public sealed record UnknownEventContext(
    Guid EventId,
    Guid StreamId,
    string StreamType,
    Guid TenantId,
    long Sequence,
    long Version,
    string LogicalTypeName,
    string ClrTypeName,
    string Data,
    DateTimeOffset Timestamp,
    EventMaterializationException Exception);
