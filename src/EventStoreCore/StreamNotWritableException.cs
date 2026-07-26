using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// The exception thrown when an append is attempted against an archived or tombstoned stream.
/// </summary>
public sealed class StreamNotWritableException : InvalidOperationException
{
    internal StreamNotWritableException(
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamLifecycleState lifecycleState)
        : base($"Stream '{streamType}/{streamId}' for tenant '{tenantId}' is {lifecycleState} and cannot accept events.")
    {
        StreamType = streamType;
        StreamId = streamId;
        TenantId = tenantId;
        LifecycleState = lifecycleState;
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
    /// Gets the lifecycle state that prevented the write.
    /// </summary>
    public StreamLifecycleState LifecycleState { get; }
}
