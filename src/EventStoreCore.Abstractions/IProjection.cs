namespace EventStoreCore.Abstractions;

/// <summary>
/// Defines a projection that can evolve a snapshot when an event is observed.
/// </summary>
/// <typeparam name="TSnapshot">The snapshot type being projected.</typeparam>
public interface IProjection<TSnapshot>
    where TSnapshot : class, new()
{
    /// <summary>
    /// Apply the event to the snapshot. Implementations perform any required persistence inside the supplied context.
    /// Projection logic should be deterministic and idempotent because inline execution can be rolled back and
    /// eventual execution can redeliver events.
    /// </summary>
    /// <param name="snapshot">The snapshot instance to mutate.</param>
    /// <param name="event">The event to apply.</param>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="ct">Cancellation token.</param>
    static abstract Task Evolve(TSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct);

    /// <summary>
    /// Clears all projection data. Called at the start of a legacy destructive rebuild.
    /// This method cannot safely implement tenant-scoped rebuilds. Configure shadow rebuilds
    /// and implement the shadow lifecycle methods for tenant-scoped or zero-downtime rebuilds.
    /// </summary>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="ct">Cancellation token.</param>
    static abstract Task ClearAsync(IProjectionContext context, CancellationToken ct);

    /// <summary>
    /// Prepares isolated storage for a shadow rebuild. The live read model must remain readable
    /// and unchanged. Implementations should be idempotent for the supplied rebuild identifier.
    /// </summary>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="rebuild">The rebuild target and checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    static virtual Task PrepareRebuildAsync(
        IProjectionContext context,
        ProjectionRebuild rebuild,
        CancellationToken ct) =>
        throw new NotSupportedException("Shadow rebuild preparation is not implemented.");

    /// <summary>
    /// Applies one replayed event to isolated shadow storage. This method owns snapshot lookup
    /// and persistence for the shadow target; EventStoreCore does not mutate the live snapshot.
    /// Implementations must be deterministic and idempotent.
    /// </summary>
    /// <param name="event">The event to replay.</param>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="rebuild">The rebuild target and checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    static virtual Task EvolveRebuildAsync(
        IEvent @event,
        IProjectionContext context,
        ProjectionRebuild rebuild,
        CancellationToken ct) =>
        throw new NotSupportedException("Shadow rebuild replay is not implemented.");

    /// <summary>
    /// Atomically makes the completed shadow target the live read model. Storage ownership stays
    /// with the projection, so EventStoreCore cannot provide a generic swap. Implementations must
    /// be idempotent because activation can be retried after cancellation or process failure.
    /// </summary>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="rebuild">The completed rebuild target and checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    static virtual Task ActivateRebuildAsync(
        IProjectionContext context,
        ProjectionRebuild rebuild,
        CancellationToken ct) =>
        throw new NotSupportedException("Shadow rebuild activation is not implemented.");

    /// <summary>
    /// Discards isolated storage for an abandoned shadow target. Implementations must be
    /// idempotent and must not modify the active read model.
    /// </summary>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="rebuild">The rebuild target and checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    static virtual Task DiscardRebuildAsync(
        IProjectionContext context,
        ProjectionRebuild rebuild,
        CancellationToken ct) =>
        throw new NotSupportedException("Shadow rebuild cleanup is not implemented.");
}
