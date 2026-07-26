using EventStoreCore.Abstractions;

namespace EventStoreCore.Testing;

/// <summary>
/// Executes projection evolution and rebuild behavior without relying on a persistence provider.
/// </summary>
/// <typeparam name="TProjection">The projection implementation to exercise.</typeparam>
/// <typeparam name="TSnapshot">The snapshot type evolved by the projection.</typeparam>
public sealed class ProjectionTestHarness<TProjection, TSnapshot>
    where TProjection : IProjection<TSnapshot>
    where TSnapshot : class, new()
{
    /// <summary>
    /// Creates a projection test harness.
    /// </summary>
    /// <param name="services">
    /// Services available through the projection context. When omitted, an empty service provider is used.
    /// </param>
    /// <param name="providerState">
    /// Optional provider-specific state exposed through the projection context. The harness does not inspect it.
    /// </param>
    public ProjectionTestHarness(IServiceProvider? services = null, object? providerState = null)
    {
        Context = new HarnessProjectionContext(services ?? EmptyServiceProvider.Instance, providerState);
    }

    /// <summary>
    /// Gets the context supplied to projection evolution and clear operations.
    /// </summary>
    public IProjectionContext Context { get; }

    /// <summary>
    /// Gets the projection version declared by <see cref="ProjectionVersionAttribute"/>, or <c>1</c> when no
    /// version attribute is present.
    /// </summary>
    public int ProjectionVersion =>
        typeof(TProjection)
            .GetCustomAttributes(typeof(ProjectionVersionAttribute), inherit: false)
            .OfType<ProjectionVersionAttribute>()
            .SingleOrDefault()
            ?.Version ?? 1;

    /// <summary>
    /// Evolves a snapshot with one event.
    /// </summary>
    /// <param name="snapshot">The snapshot to evolve.</param>
    /// <param name="event">The event to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when projection evolution finishes.</returns>
    public Task EvolveAsync(TSnapshot snapshot, IEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(@event);

        return TProjection.Evolve(snapshot, @event, Context, ct);
    }

    /// <summary>
    /// Evolves a snapshot with events in enumeration order.
    /// </summary>
    /// <param name="snapshot">The snapshot to evolve.</param>
    /// <param name="events">The ordered events to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every event has been applied.</returns>
    public async Task EvolveAsync(
        TSnapshot snapshot,
        IEnumerable<IEvent> events,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(events);

        foreach (var @event in events)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(@event);
            await TProjection.Evolve(snapshot, @event, Context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invokes the projection's clear behavior with the configured projection context.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the projection has cleared its data.</returns>
    public Task ClearAsync(CancellationToken ct = default)
    {
        return TProjection.ClearAsync(Context, ct);
    }

    /// <summary>
    /// Clears projection data and then replays events in enumeration order.
    /// </summary>
    /// <param name="events">The ordered events to replay after clearing.</param>
    /// <param name="snapshotResolver">
    /// Resolves the snapshot to evolve for each event. The resolver controls snapshot identity and storage semantics.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when clear and replay finish.</returns>
    /// <remarks>
    /// Clear always completes before the first snapshot is resolved. The harness does not apply projection
    /// registration filters or infer snapshot keys.
    /// </remarks>
    public async Task RebuildAsync(
        IEnumerable<IEvent> events,
        Func<IEvent, TSnapshot> snapshotResolver,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(snapshotResolver);

        await TProjection.ClearAsync(Context, ct).ConfigureAwait(false);

        foreach (var @event in events)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(@event);

            var snapshot = snapshotResolver(@event)
                ?? throw new InvalidOperationException("The snapshot resolver returned null.");

            await TProjection.Evolve(snapshot, @event, Context, ct).ConfigureAwait(false);
        }
    }

    private sealed class HarnessProjectionContext(
        IServiceProvider services,
        object? providerState) : IProjectionContext
    {
        public IServiceProvider Services { get; } = services;

        public object? ProviderState { get; } = providerState;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
