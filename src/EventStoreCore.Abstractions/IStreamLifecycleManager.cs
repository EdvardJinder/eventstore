namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides explicit administrative access to stream lifecycle state.
/// Lifecycle operations change metadata only and never delete or rewrite event payloads.
/// </summary>
public interface IStreamLifecycleManager
{
    /// <summary>
    /// Gets lifecycle metadata for a stream, including tombstoned streams.
    /// </summary>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lifecycle metadata, or <see langword="null"/> when the stream does not exist.</returns>
    Task<StreamLifecycleInfo?> GetAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives an active stream at an exact event version.
    /// </summary>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="expectedVersion">The exact current event version required for the transition.</param>
    /// <param name="change">Required audit metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated lifecycle metadata.</returns>
    Task<StreamLifecycleInfo> ArchiveAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleChange change,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archived stream at an exact event version.
    /// Tombstoned streams cannot be restored.
    /// </summary>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="expectedVersion">The exact current event version required for the transition.</param>
    /// <param name="change">Required audit metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated lifecycle metadata.</returns>
    Task<StreamLifecycleInfo> RestoreAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleChange change,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Irreversibly tombstones an active or archived stream at an exact event version.
    /// The event history remains physically retained and continues to keep its global log positions.
    /// </summary>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="expectedVersion">The exact current event version required for the transition.</param>
    /// <param name="change">Required audit metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated lifecycle metadata.</returns>
    Task<StreamLifecycleInfo> TombstoneAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleChange change,
        CancellationToken cancellationToken = default);
}
