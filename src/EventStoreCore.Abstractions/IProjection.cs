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
    /// </summary>
    /// <param name="snapshot">The snapshot instance to mutate.</param>
    /// <param name="event">The event to apply.</param>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="ct">Cancellation token.</param>
    static abstract Task Evolve(TSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct);

    /// <summary>
    /// Clears all projection data. Called at the start of a rebuild operation.
    /// </summary>
    /// <param name="context">Provider-specific context for projection execution.</param>
    /// <param name="ct">Cancellation token.</param>
    static abstract Task ClearAsync(IProjectionContext context, CancellationToken ct);
}
