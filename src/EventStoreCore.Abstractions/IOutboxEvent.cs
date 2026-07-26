namespace EventStoreCore.Abstractions;

/// <summary>
/// Describes a domain event captured from an EF entity change.
/// </summary>
public interface IOutboxEvent
{
    /// <summary>
    /// The unique event identifier.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The outbox sequence used for ordered reading and checkpoints.
    /// </summary>
    long Sequence { get; }

    /// <summary>
    /// The event payload.
    /// </summary>
    object Data { get; }

    /// <summary>
    /// The CLR type of the event payload.
    /// </summary>
    Type EventType { get; }

    /// <summary>
    /// When the event was captured in UTC.
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// The tenant identifier.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// The assembly-qualified CLR type name of the source entity.
    /// </summary>
    string SourceEntityType { get; }

    /// <summary>
    /// A JSON object containing the source entity's primary-key values.
    /// </summary>
    string SourceEntityKey { get; }

    /// <summary>
    /// The entity change that produced the event.
    /// </summary>
    EntityChangeKind ChangeKind { get; }
}

/// <summary>
/// Describes a strongly typed domain event captured from an EF entity change.
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public interface IOutboxEvent<out T> : IOutboxEvent
    where T : class
{
    /// <summary>
    /// The strongly typed event payload.
    /// </summary>
    new T Data { get; }
}
