namespace EventStoreCore.Persistence.EntityFrameworkCore;

/// <summary>
/// Configuration options for the projection daemon.
/// </summary>
public sealed class ProjectionDaemonOptions
{
    /// <summary>
    /// The number of events to process in each batch during rebuilds and catch-up.
    /// Default is 500.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How often the daemon polls for new events when caught up.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time to hold a distributed lock for a projection.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to automatically rebuild projections when their version changes.
    /// Default is true.
    /// </summary>
    public bool AutoRebuildOnVersionChange { get; set; } = true;

    /// <summary>
    /// How long to wait before retrying after an error.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional delay between batches during rebuild (for throttling).
    /// Default is no delay.
    /// </summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.Zero;
}
