namespace EventStoreCore.Abstractions;

/// <summary>
/// A bounded page from the global event log.
/// </summary>
public sealed class EventLogPage
{
    /// <summary>
    /// Creates a global event-log page.
    /// </summary>
    /// <param name="events">Events in ascending global sequence order.</param>
    /// <param name="headSequence">
    /// The highest visible sequence captured before the page query and bounded by the requested upper sequence.
    /// </param>
    /// <param name="nextSequence">
    /// The exclusive lower bound to use for the next page, or null when the filtered range is exhausted.
    /// </param>
    public EventLogPage(
        IReadOnlyList<IEvent> events,
        long headSequence,
        long? nextSequence)
    {
        ArgumentNullException.ThrowIfNull(events);
        Events = events;
        HeadSequence = headSequence;
        NextSequence = nextSequence;
    }

    /// <summary>
    /// Events in ascending global sequence order.
    /// </summary>
    public IReadOnlyList<IEvent> Events { get; }

    /// <summary>
    /// The highest visible sequence captured before the page query. Events allocated a higher sequence are excluded,
    /// and an explicit upper sequence can lower this value.
    /// </summary>
    public long HeadSequence { get; }

    /// <summary>
    /// The exclusive lower bound to use for the next page, or null when the filtered range is exhausted.
    /// </summary>
    public long? NextSequence { get; }

    /// <summary>
    /// Indicates whether another page exists in the filtered range.
    /// </summary>
    public bool HasMore => NextSequence.HasValue;
}
