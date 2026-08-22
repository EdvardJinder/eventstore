namespace EventStoreCore;

/// <summary>
/// Identifies event sources eligible for inline event-handler delivery.
/// </summary>
[Flags]
public enum InlineEventSource
{
    /// <summary>Events appended to EventStoreCore streams.</summary>
    Stream = 1,

    /// <summary>Events captured from ordinary EF entity changes into the outbox.</summary>
    EntityOutbox = 2,

    /// <summary>Events from streams and entity-outbox capture.</summary>
    All = Stream | EntityOutbox
}
