using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Configures aggregate snapshots for event streams.
/// </summary>
public interface ISnapshotBuilder
{
    /// <summary>
    /// Enables snapshots for a state type and the default stream type.
    /// </summary>
    /// <typeparam name="TState">The state type to snapshot.</typeparam>
    /// <param name="configure">Optional snapshot configuration.</param>
    /// <returns>The snapshot builder for chaining.</returns>
    ISnapshotBuilder For<TState>(Action<SnapshotOptions>? configure = null)
        where TState : IState, new();

    /// <summary>
    /// Enables snapshots for a state type and stream type.
    /// </summary>
    /// <typeparam name="TState">The state type to snapshot.</typeparam>
    /// <param name="streamType">The stream type whose events hydrate the state.</param>
    /// <param name="configure">Optional snapshot configuration.</param>
    /// <returns>The snapshot builder for chaining.</returns>
    ISnapshotBuilder For<TState>(string streamType, Action<SnapshotOptions>? configure = null)
        where TState : IState, new();
}
