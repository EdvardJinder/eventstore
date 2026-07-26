using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Provides raw persisted data for an outbox event that could not be materialized.
/// </summary>
/// <param name="EventId">The stable outbox event identifier.</param>
/// <param name="Sequence">The outbox sequence.</param>
/// <param name="LogicalTypeName">The persisted logical event type.</param>
/// <param name="ClrTypeName">The persisted CLR type name.</param>
/// <param name="Data">The serialized event payload.</param>
/// <param name="Timestamp">When the event was captured.</param>
/// <param name="TenantId">The event tenant.</param>
/// <param name="SourceEntityType">The source entity CLR type name.</param>
/// <param name="SourceEntityKey">The serialized source entity key.</param>
/// <param name="ChangeKind">The source entity change kind.</param>
/// <param name="Exception">The materialization failure.</param>
public sealed record UnknownOutboxEventContext(
    Guid EventId,
    long Sequence,
    string LogicalTypeName,
    string ClrTypeName,
    string Data,
    DateTimeOffset Timestamp,
    Guid TenantId,
    string SourceEntityType,
    string SourceEntityKey,
    EntityChangeKind ChangeKind,
    Exception Exception);
