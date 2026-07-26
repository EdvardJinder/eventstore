namespace EventStoreCore.Abstractions;

/// <summary>
/// Describes one immutable stream lifecycle audit entry.
/// </summary>
public sealed class StreamLifecycleEntry
{
    /// <summary>
    /// Gets the unique audit entry identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the state before the transition.
    /// </summary>
    public required StreamLifecycleState FromState { get; init; }

    /// <summary>
    /// Gets the state after the transition.
    /// </summary>
    public required StreamLifecycleState ToState { get; init; }

    /// <summary>
    /// Gets the stream version against which the transition was authorized.
    /// </summary>
    public required long StreamVersion { get; init; }

    /// <summary>
    /// Gets when the transition occurred in UTC.
    /// </summary>
    public required DateTimeOffset ChangedAtUtc { get; init; }

    /// <summary>
    /// Gets the identity that authorized the transition.
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>
    /// Gets the reason for the transition.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the optional application correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }
}
