namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides management operations for projections including status monitoring and rebuild control.
/// </summary>
public interface IProjectionManager
{
    /// <summary>
    /// Gets the status of a specific projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The projection status, or null if not found.</returns>
    Task<ProjectionStatusDto?> GetStatusAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant-scoped status of a specific projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The projection status, or null if not found.</returns>
    Task<ProjectionStatusDto?> GetStatusAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection status is not supported by this manager.");
    }

    /// <summary>
    /// Gets the status of all registered projections.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of all projection statuses.</returns>
    Task<IReadOnlyList<ProjectionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant-scoped status of all registered projections for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of projection statuses for the tenant.</returns>
    Task<IReadOnlyList<ProjectionStatusDto>> GetAllStatusesAsync(Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection status is not supported by this manager.");
    }

    /// <summary>
    /// Triggers a rebuild of the specified projection.
    /// This will clear all projection data and replay all events from the beginning.
    /// </summary>
    /// <param name="projectionName">The name of the projection to rebuild.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RebuildAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Pauses processing of the specified projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection to pause.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Pauses tenant-scoped processing of the specified projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection to pause.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection pause is not supported by this manager.");
    }

    /// <summary>
    /// Resumes processing of a paused projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResumeAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Resumes tenant-scoped processing of a paused projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection to resume.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResumeAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection resume is not supported by this manager.");
    }

    /// <summary>
    /// Retries processing the failed event for the specified projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RetryFailedEventAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed tenant-scoped event for the specified projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RetryFailedEventAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection retry is not supported by this manager.");
    }

    /// <summary>
    /// Skips the failed event and resumes processing from the next event.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SkipFailedEventAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed tenant-scoped event and resumes processing from the next event.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SkipFailedEventAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection skip is not supported by this manager.");
    }

    /// <summary>
    /// Gets details about the failed event for a faulted projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Details about the failed event, or null if the projection is not faulted.</returns>
    Task<FailedEventDto?> GetFailedEventAsync(string projectionName, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed tenant-scoped event for a faulted projection.
    /// </summary>
    /// <param name="projectionName">The name of the projection.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Details about the failed event, or null if the projection is not faulted.</returns>
    Task<FailedEventDto?> GetFailedEventAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped projection failed-event lookup is not supported by this manager.");
    }
}

/// <summary>
/// Represents the current status of a projection.
/// </summary>
/// <param name="ProjectionName">The unique name of the projection.</param>
/// <param name="Version">The current version of the projection.</param>
/// <param name="State">The current state of the projection.</param>
/// <param name="Position">The sequence number of the last processed event.</param>
/// <param name="TotalEvents">The total number of events in the event store (for progress calculation).</param>
/// <param name="ProgressPercentage">The rebuild/catchup progress as a percentage (0-100).</param>
/// <param name="LastProcessedAt">When the last event was processed.</param>
/// <param name="LastError">The last error message if the projection is faulted.</param>
/// <param name="FailedEventSequence">The sequence number of the event that caused a fault.</param>
/// <param name="RebuildStartedAt">When the current/last rebuild started.</param>
/// <param name="RebuildCompletedAt">When the last rebuild completed.</param>
public sealed record ProjectionStatusDto(
    string ProjectionName,
    int Version,
    ProjectionState State,
    long Position,
    long? TotalEvents,
    double? ProgressPercentage,
    DateTimeOffset? LastProcessedAt,
    string? LastError,
    long? FailedEventSequence,
    DateTimeOffset? RebuildStartedAt,
    DateTimeOffset? RebuildCompletedAt
)
{
    /// <summary>
    /// Whether this status row is global or tenant-scoped.
    /// </summary>
    public CheckpointScope CheckpointScope { get; init; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant id for tenant-scoped status rows, or null for global status rows.
    /// </summary>
    public Guid? TenantId { get; init; }
}

/// <summary>
/// Represents the possible states of a projection.
/// </summary>
public enum ProjectionState
{
    /// <summary>
    /// The projection is operating normally.
    /// </summary>
    Active = 0,

    /// <summary>
    /// The projection is being rebuilt from the beginning.
    /// </summary>
    Rebuilding = 1,

    /// <summary>
    /// The projection has been manually paused.
    /// </summary>
    Paused = 2,

    /// <summary>
    /// The projection encountered an error and requires intervention.
    /// </summary>
    Faulted = 3
}

/// <summary>
/// Contains details about a failed event for diagnostic purposes.
/// </summary>
/// <param name="EventId">The unique identifier of the event.</param>
/// <param name="StreamId">The stream the event belongs to.</param>
/// <param name="Version">The version of the event within its stream.</param>
/// <param name="Sequence">The global sequence number of the event.</param>
/// <param name="EventType">The type name of the event.</param>
/// <param name="Data">The serialized event data as JSON.</param>
/// <param name="Timestamp">When the event was created.</param>
/// <param name="ProjectionError">The error message from the projection.</param>
public sealed record FailedEventDto(
    Guid EventId,
    Guid StreamId,
    long Version,
    long Sequence,
    string EventType,
    string Data,
    DateTimeOffset Timestamp,
    string ProjectionError
)
{
    /// <summary>
    /// Whether the failed event belongs to a global or tenant-scoped checkpoint.
    /// </summary>
    public CheckpointScope CheckpointScope { get; init; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant id for tenant-scoped failures, or null for global failures.
    /// </summary>
    public Guid? TenantId { get; init; }
}
