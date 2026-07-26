namespace EventStoreCore.Abstractions;

/// <summary>
/// A bounded page of events from a stream.
/// </summary>
public sealed class StreamPage
{
    /// <summary>
    /// Creates a stream page.
    /// </summary>
    /// <param name="events">Events in the requested ordering.</param>
    /// <param name="streamVersion">The stream version captured before the page query.</param>
    /// <param name="nextVersion">The inclusive cursor for the next page, or null when the range is exhausted.</param>
    public StreamPage(IReadOnlyList<IEvent> events, long streamVersion, long? nextVersion)
    {
        ArgumentNullException.ThrowIfNull(events);
        Events = events;
        StreamVersion = streamVersion;
        NextVersion = nextVersion;
    }

    /// <summary>
    /// Events in the requested ordering.
    /// </summary>
    public IReadOnlyList<IEvent> Events { get; }

    /// <summary>
    /// The stream version captured before the page query. Events appended later are excluded.
    /// </summary>
    public long StreamVersion { get; }

    /// <summary>
    /// The inclusive cursor for the next page, or null when the requested range is exhausted.
    /// </summary>
    public long? NextVersion { get; }

    /// <summary>
    /// Indicates whether another page exists in the requested range.
    /// </summary>
    public bool HasMore => NextVersion.HasValue;
}
