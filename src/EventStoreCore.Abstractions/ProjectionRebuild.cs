namespace EventStoreCore.Abstractions;

/// <summary>
/// Identifies an isolated projection rebuild target.
/// </summary>
/// <param name="Id">Stable identifier for the rebuild target.</param>
/// <param name="TargetVersion">Projection version being built.</param>
/// <param name="CheckpointScope">Whether replay is global or tenant-scoped.</param>
/// <param name="TenantId">Tenant identifier for a tenant-scoped rebuild; otherwise <see langword="null"/>.</param>
public sealed record ProjectionRebuild(
    Guid Id,
    int TargetVersion,
    CheckpointScope CheckpointScope,
    Guid? TenantId);
