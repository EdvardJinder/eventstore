namespace EventStoreCore.Abstractions;

/// <summary>
/// A bounded page whose event payloads share a requested contract.
/// </summary>
/// <typeparam name="TEvent">The requested event payload contract.</typeparam>
public sealed class StreamPage<TEvent>
    where TEvent : class
{
    /// <summary>
    /// Creates a typed stream page.
    /// </summary>
    public StreamPage(
        IReadOnlyList<IEvent<TEvent>> events,
        long streamVersion,
        long? nextVersion)
    {
        ArgumentNullException.ThrowIfNull(events);
        Events = events;
        StreamVersion = streamVersion;
        NextVersion = nextVersion;
    }

    /// <summary>
    /// Events in the requested ordering.
    /// </summary>
    public IReadOnlyList<IEvent<TEvent>> Events { get; }

    /// <summary>
    /// The captured stream version.
    /// </summary>
    public long StreamVersion { get; }

    /// <summary>
    /// The inclusive cursor for the next page, or null when exhausted.
    /// </summary>
    public long? NextVersion { get; }

    /// <summary>
    /// Indicates whether another page exists.
    /// </summary>
    public bool HasMore => NextVersion.HasValue;
}
