using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Represents the persistent state of a projection for tracking position, version, and status.
/// </summary>
public sealed class DbProjectionStatus
{
    /// <summary>
    /// The unique name of the projection (typically the fully qualified type name).
    /// </summary>
    public string ProjectionName { get; set; } = null!;

    /// <summary>
    /// Whether this checkpoint is shared globally or belongs to a tenant.
    /// </summary>
    public CheckpointScope CheckpointScope { get; set; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant this checkpoint belongs to when <see cref="CheckpointScope" /> is <see cref="Abstractions.CheckpointScope.Tenant" />.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;

    /// <summary>
    /// The version of the projection. Used to detect when a projection needs rebuilding.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The current state of the projection.
    /// </summary>
    public ProjectionState State { get; set; } = ProjectionState.Active;

    /// <summary>
    /// The sequence number of the last successfully processed event.
    /// </summary>
    public long Position { get; set; } = 0;

    /// <summary>
    /// Cached total number of events for progress calculation.
    /// </summary>
    public long? TotalEvents { get; set; }

    /// <summary>
    /// When the projection last successfully processed an event.
    /// </summary>
    public DateTimeOffset? LastProcessedAt { get; set; }

    /// <summary>
    /// The error message if the projection is in a faulted state.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The sequence number of the event that caused the projection to fault.
    /// </summary>
    public long? FailedEventSequence { get; set; }

    /// <summary>
    /// When the current or most recent rebuild operation started.
    /// </summary>
    public DateTimeOffset? RebuildStartedAt { get; set; }

    /// <summary>
    /// When the most recent rebuild operation completed.
    /// </summary>
    public DateTimeOffset? RebuildCompletedAt { get; set; }

    /// <summary>
    /// Converts this entity to a DTO for external consumption.
    /// </summary>
    public ProjectionStatusDto ToDto()
    {
        var progress = CheckpointScope == Abstractions.CheckpointScope.Global && TotalEvents.HasValue && TotalEvents.Value > 0
            ? Math.Round((double)Position / TotalEvents.Value * 100, 2)
            : (double?)null;

        return new ProjectionStatusDto(
            ProjectionName,
            Version,
            State,
            Position,
            TotalEvents,
            progress,
            LastProcessedAt,
            LastError,
            FailedEventSequence,
            RebuildStartedAt,
            RebuildCompletedAt
        )
        {
            CheckpointScope = CheckpointScope,
            TenantId = CheckpointScope == Abstractions.CheckpointScope.Tenant ? TenantId : null
        };
    }
}
