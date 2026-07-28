using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Configuration options for subscription processing.
/// </summary>
public sealed class SubscriptionOptions
{
    /// <summary>
    /// The maximum number of subscription checkpoint workers that may process batches concurrently.
    /// Polling and retry delays do not consume concurrency slots. The value must be positive.
    /// </summary>
    public int MaxConcurrentWorkers { get; set; } = 8;

    /// <summary>
    /// The positive number of events to read and process in each daemon batch.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// The positive number of processed events to handle before persisting the subscription checkpoint.
    /// </summary>
    public int CheckpointFrequency { get; set; } = 1;

    /// <summary>
    /// How often to poll for new events when caught up. The value must be non-negative.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum time to wait to acquire a distributed lock for a subscription.
    /// Lock acquisition does not consume worker capacity. The acquired lock is held
    /// until its handle is disposed.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The positive maximum retry attempts before giving up in the daemon loop.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Determines whether subscription checkpoints are shared globally or stored separately per tenant.
    /// </summary>
    public CheckpointScope CheckpointScope { get; set; } = CheckpointScope.Global;

    /// <summary>
    /// Delay between retry attempts when processing fails. The value must be non-negative.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}
