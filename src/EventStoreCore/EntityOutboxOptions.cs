using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Controls entity-outbox daemon batching, retries, polling, and checkpoint isolation.
/// </summary>
public sealed class EntityOutboxOptions
{
    /// <summary>
    /// The maximum number of outbox checkpoint workers that may process batches concurrently.
    /// Polling and retry delays do not consume concurrency slots. The value must be positive.
    /// </summary>
    public int MaxConcurrentWorkers { get; set; } = 8;

    /// <summary>
    /// The positive maximum number of messages processed per batch.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How often a caught-up daemon polls for new messages. The value must be non-negative.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The delay before retrying a failed message. The value must be non-negative.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum time spent waiting to acquire a subscription's distributed lock.
    /// Lock acquisition does not consume worker capacity.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The positive number of failed attempts before a checkpoint is dead-lettered.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Determines whether each subscription uses a global or per-tenant checkpoint.
    /// </summary>
    public CheckpointScope CheckpointScope { get; set; } = CheckpointScope.Global;
}
