using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;


/// <summary>
/// Internal registration information for a projection used by the daemon.
/// </summary>
internal sealed class ProjectionRegistration
{
    /// <summary>
    /// The unique name of the projection (typically the fully qualified type name).
    /// </summary>
    public required string Name { get; init; }

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
    public required Func<DbContext, CancellationToken, Task> ClearAction { get; init; }

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
