namespace EventStoreCore.Abstractions;

/// <summary>
/// Describes the current lifecycle state and immutable audit history of a stream.
/// </summary>
public sealed class StreamLifecycleInfo
{
    /// <summary>
    /// Gets the logical stream type.
    /// </summary>
    public required string StreamType { get; init; }

    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required Guid StreamId { get; init; }

    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Gets the current event stream version.
    /// </summary>
    public required long StreamVersion { get; init; }

    /// <summary>
    /// Gets the current lifecycle state.
    /// </summary>
    public required StreamLifecycleState State { get; init; }

    /// <summary>
    /// Gets when the stream was created in UTC.
    /// </summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets when the stream was most recently updated in UTC.
    /// </summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>
    /// Gets the lifecycle audit entries in transition order.
    /// </summary>
    public required IReadOnlyList<StreamLifecycleEntry> History { get; init; }
}
