namespace EventStoreCore.Abstractions;

/// <summary>
/// Helpers for associating event payloads with append metadata.
/// </summary>
public static class EventAppendExtensions
{
    /// <summary>
    /// Associates a payload with metadata for an append operation.
    /// </summary>
    /// <param name="eventData">The event payload.</param>
    /// <param name="metadata">The metadata to persist.</param>
    /// <returns>An append envelope accepted by existing append APIs.</returns>
    public static EventToAppend WithMetadata(this object eventData, EventMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ArgumentNullException.ThrowIfNull(metadata);

        return eventData is EventToAppend append
            ? append with { Metadata = metadata }
            : new EventToAppend(eventData, metadata);
    }

    /// <summary>
    /// Associates a payload with a caller-supplied globally unique event identifier.
    /// </summary>
    /// <param name="eventData">The event payload or an existing append envelope.</param>
    /// <param name="eventId">The globally unique event identifier.</param>
    /// <returns>An append envelope accepted by append APIs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="eventData" /> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="eventId" /> is empty.</exception>
    public static EventToAppend WithEventId(this object eventData, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event identifiers cannot be empty.", nameof(eventId));
        }

        return eventData is EventToAppend append
            ? append with { EventId = eventId }
            : new EventToAppend(eventData, new EventMetadata(), eventId);
    }
}
