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
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(metadata);
        Data = data;
        Metadata = metadata;
    }

    /// <summary>
    /// The event payload.
    /// </summary>
    public object Data { get; init; } = null!;

    /// <summary>
    /// The metadata to persist with the event.
    /// </summary>
    public EventMetadata Metadata { get; init; } = null!;
}
