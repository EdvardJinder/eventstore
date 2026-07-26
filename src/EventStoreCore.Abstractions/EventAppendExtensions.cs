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
        => new(eventData, metadata);
}
