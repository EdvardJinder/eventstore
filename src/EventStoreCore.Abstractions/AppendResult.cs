namespace EventStoreCore.Abstractions;

/// <summary>
/// Identifies one event committed by an append operation.
/// </summary>
public sealed record AppendedEventInfo
{
    /// <summary>
    /// Creates committed event information.
    /// </summary>
    /// <param name="eventId">The stable event identifier.</param>
    /// <param name="streamVersion">The event version within its stream.</param>
    /// <param name="globalSequence">The event sequence in the global log.</param>
    public AppendedEventInfo(Guid eventId, long streamVersion, long globalSequence)
    {
        EventId = eventId;
        StreamVersion = streamVersion;
        GlobalSequence = globalSequence;
    }

    /// <summary>
    /// The stable event identifier.
    /// </summary>
    public Guid EventId { get; }

    /// <summary>
    /// The event version within its stream.
    /// </summary>
    public long StreamVersion { get; }

    /// <summary>
    /// The event sequence in the global log.
    /// </summary>
    public long GlobalSequence { get; }
}

/// <summary>
/// Describes the committed result of an append without materializing the stream.
/// </summary>
public sealed record AppendResult
{
    /// <summary>
    /// Creates an append result.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="previousVersion">The stream version immediately before this append.</param>
    /// <param name="currentVersion">The stream version immediately after this append.</param>
    /// <param name="events">The events committed by this append.</param>
    /// <param name="wasAlreadyCommitted">Whether this call recovered an earlier committed result.</param>
    public AppendResult(
        Guid streamId,
        string streamType,
        Guid tenantId,
        long previousVersion,
        long currentVersion,
        IReadOnlyList<AppendedEventInfo> events,
        bool wasAlreadyCommitted)
    {
        ArgumentNullException.ThrowIfNull(streamType);
        ArgumentNullException.ThrowIfNull(events);
        StreamId = streamId;
        StreamType = streamType;
        TenantId = tenantId;
        PreviousVersion = previousVersion;
        CurrentVersion = currentVersion;
        Events = Array.AsReadOnly(events.ToArray());
        WasAlreadyCommitted = wasAlreadyCommitted;
    }

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// The logical stream type.
    /// </summary>
    public string StreamType { get; }

    /// <summary>
    /// The tenant identifier.
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// The stream version immediately before this append.
    /// </summary>
    public long PreviousVersion { get; }

    /// <summary>
    /// The stream version immediately after this append.
    /// </summary>
    public long CurrentVersion { get; }

    /// <summary>
    /// The events committed by this append in stream order.
    /// </summary>
    public IReadOnlyList<AppendedEventInfo> Events { get; }

    /// <summary>
    /// Whether this call recovered a result committed by an earlier attempt.
    /// </summary>
    public bool WasAlreadyCommitted { get; }
}
