namespace EventStoreCore.Abstractions;

/// <summary>
/// Describes one atomic append that returns a compact committed result.
/// </summary>
/// <remarks>
/// Assign a caller-supplied identifier to every event when the append must be
/// safely retryable. An exact retry returns the original committed result.
/// </remarks>
public sealed class AppendOperation
{
    /// <summary>
    /// Creates an append operation for the default stream type and tenant.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="expectedVersion">The optimistic concurrency expectation.</param>
    /// <param name="events">The ordered events to append.</param>
    public AppendOperation(
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        StreamId = streamId;
        ExpectedVersion = expectedVersion;
        Events = Array.AsReadOnly(events.ToArray());
    }

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// The logical stream type.
    /// </summary>
    public string StreamType { get; init; } = string.Empty;

    /// <summary>
    /// The tenant identifier.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// The optimistic concurrency expectation applied only to the first execution.
    /// </summary>
    public ExpectedVersion ExpectedVersion { get; }

    /// <summary>
    /// The ordered events to append.
    /// </summary>
    public IReadOnlyList<object> Events { get; }
}
