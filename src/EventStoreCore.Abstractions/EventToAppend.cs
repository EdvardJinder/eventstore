namespace EventStoreCore.Abstractions;

/// <summary>
/// Associates an event payload with application-supplied metadata for an append.
/// </summary>
public sealed record EventToAppend
{
    /// <summary>
    /// Creates an append envelope.
    /// </summary>
    /// <param name="data">The event payload.</param>
    /// <param name="metadata">The metadata to persist with the event.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public EventToAppend(object data, EventMetadata metadata)
        : this(data, metadata, eventId: null)
    {
    }

    /// <summary>
    /// Creates an append envelope with a caller-supplied event identifier.
    /// </summary>
    /// <param name="data">The event payload.</param>
    /// <param name="metadata">The metadata to persist with the event.</param>
    /// <param name="eventId">The globally unique event identifier.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data" /> or <paramref name="metadata" /> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="eventId" /> is empty.</exception>
    public EventToAppend(object data, EventMetadata metadata, Guid eventId)
        : this(data, metadata, (Guid?)eventId)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event identifiers cannot be empty.", nameof(eventId));
        }
    }

    private EventToAppend(object data, EventMetadata metadata, Guid? eventId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(metadata);
        Data = data;
        Metadata = metadata;
        EventId = eventId;
    }

    /// <summary>
    /// The event payload.
    /// </summary>
    public object Data { get; init; } = null!;

    /// <summary>
    /// The metadata to persist with the event.
    /// </summary>
    public EventMetadata Metadata { get; init; } = null!;

    /// <summary>
    /// The caller-supplied globally unique event identifier, or null to generate one during append.
    /// </summary>
    public Guid? EventId { get; init; }
}
