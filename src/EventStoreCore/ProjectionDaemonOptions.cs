using EventStoreCore.Abstractions;

namespace EventStoreCore;


/// <summary>
/// Configuration options for the projection daemon.
/// </summary>
public sealed class ProjectionDaemonOptions
{
    /// <summary>
    /// The maximum number of projection checkpoint workers that may process batches concurrently.
    /// Polling and retry delays do not consume concurrency slots. The value must be positive.
    /// </summary>
    public int MaxConcurrentWorkers { get; set; } = 8;

    /// <summary>
    /// The positive number of events to process in each batch during rebuilds and catch-up.
    /// Default is 500.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How often the daemon polls for new events when caught up. The value must be non-negative.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time to wait to acquire a distributed lock for a projection.
    /// Lock acquisition does not consume worker capacity. The acquired lock is held
    /// until its handle is disposed.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to automatically rebuild projections when their version changes.
    /// Default is true.
    /// </summary>
    public bool AutoRebuildOnVersionChange { get; set; } = true;

    /// <summary>
    /// How long to wait before retrying after an error. The value must be non-negative.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Determines whether projection checkpoints are shared globally or stored separately per tenant.
    /// Default is <see cref="CheckpointScope.Global" />.
    /// </summary>
    public CheckpointScope CheckpointScope { get; set; } = CheckpointScope.Global;

    /// <summary>
    /// Optional non-negative delay between batches during rebuild (for throttling).
    /// Default is no delay.
    /// </summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.Zero;
}
