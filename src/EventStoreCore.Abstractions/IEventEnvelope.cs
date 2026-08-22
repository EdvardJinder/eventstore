namespace EventStoreCore.Abstractions;

/// <summary>
/// Describes the common identity and payload exposed by an event source.
/// </summary>
public interface IEventEnvelope
{
    /// <summary>The unique event identifier.</summary>
    Guid Id { get; }

    /// <summary>The event payload.</summary>
    object Data { get; }

    /// <summary>The CLR type of the event payload.</summary>
    Type EventType { get; }

    /// <summary>When the event was created in UTC.</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>The tenant identifier.</summary>
    Guid TenantId { get; }
}

/// <summary>
/// Describes the common identity and strongly typed payload exposed by an event source.
/// </summary>
/// <typeparam name="TEvent">The event payload type.</typeparam>
public interface IEventEnvelope<out TEvent> : IEventEnvelope
    where TEvent : class
{
    /// <summary>The strongly typed event payload.</summary>
    new TEvent Data { get; }

    object IEventEnvelope.Data => Data;
}
