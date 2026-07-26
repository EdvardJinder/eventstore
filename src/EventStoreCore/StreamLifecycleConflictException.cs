using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// The exception thrown when a stream lifecycle transition loses an optimistic concurrency check
/// or is invalid for the current lifecycle state.
/// </summary>
public sealed class StreamLifecycleConflictException : InvalidOperationException
{
    internal StreamLifecycleConflictException(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        long? actualVersion,
        StreamLifecycleState? actualState,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StreamType = streamType;
        StreamId = streamId;
        TenantId = tenantId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
        ActualState = actualState;
    }

    /// <summary>
    /// Gets the logical stream type.
    /// </summary>
    public string StreamType { get; }

    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// Gets the exact stream version required by the operation.
    /// </summary>
    public long ExpectedVersion { get; }

    /// <summary>
    /// Gets the observed stream version, or <see langword="null"/> when the stream was not found.
    /// </summary>
    public long? ActualVersion { get; }

    /// <summary>
    /// Gets the observed lifecycle state, or <see langword="null"/> when the stream was not found.
    /// </summary>
    public StreamLifecycleState? ActualState { get; }
}
