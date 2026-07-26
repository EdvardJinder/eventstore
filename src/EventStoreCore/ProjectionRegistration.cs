using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;


/// <summary>
/// Internal registration information for a projection.
/// </summary>
internal sealed class ProjectionRegistration
{
    /// <summary>
    /// The unique name of the projection (typically the fully qualified type name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The configured execution mode.
    /// </summary>
    public required ProjectionMode Mode { get; init; }

    /// <summary>
    /// The version of the projection from the attribute or options.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// The projection type.
    /// </summary>
    public required Type ProjectionType { get; init; }

    /// <summary>
    /// The snapshot entity type.
    /// </summary>
    public required Type SnapshotType { get; init; }

    /// <summary>
    /// The configured projection options.
    /// </summary>
    public required ProjectionOptions Options { get; init; }

    /// <summary>
    /// Action to clear all projection data via IProjection.ClearAsync.
    /// </summary>
    public required Func<DbContext, IServiceProvider, CancellationToken, Task> ClearAction { get; init; }

    /// <summary>
    /// Action to prepare projection-owned shadow storage.
    /// </summary>
    public Func<DbContext, IServiceProvider, ProjectionRebuild, CancellationToken, Task> PrepareRebuildAction { get; init; } =
        static (_, _, _, _) => throw new NotSupportedException("Shadow rebuild preparation is not configured.");

    /// <summary>
    /// Action to apply an event to projection-owned shadow storage.
    /// </summary>
    public Func<DbContext, IServiceProvider, IEvent, ProjectionRebuild, CancellationToken, Task> EvolveRebuildAction { get; init; } =
        static (_, _, _, _, _) => throw new NotSupportedException("Shadow rebuild replay is not configured.");

    /// <summary>
    /// Action to atomically activate projection-owned shadow storage.
    /// </summary>
    public Func<DbContext, IServiceProvider, ProjectionRebuild, CancellationToken, Task> ActivateRebuildAction { get; init; } =
        static (_, _, _, _) => throw new NotSupportedException("Shadow rebuild activation is not configured.");

    /// <summary>
    /// Action to discard projection-owned shadow storage.
    /// </summary>
    public Func<DbContext, IServiceProvider, ProjectionRebuild, CancellationToken, Task> DiscardRebuildAction { get; init; } =
        static (_, _, _, _) => throw new NotSupportedException("Shadow rebuild cleanup is not configured.");

    /// <summary>
    /// Action to evolve a snapshot with an event via IProjection.Evolve.
    /// </summary>
    public required Func<DbContext, IServiceProvider, object, IEvent, CancellationToken, Task> EvolveAction { get; init; }

    /// <summary>
    /// Function to find or create a snapshot by key.
    /// </summary>
    public required Func<DbContext, object, CancellationToken, Task<object>> GetOrCreateSnapshotAction { get; init; }

    /// <summary>
    /// Action to add a new snapshot to the context.
    /// </summary>
    public required Action<DbContext, object> AddSnapshotAction { get; init; }
}
