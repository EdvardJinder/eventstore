/// <summary>
/// Configuration options for subscription processing.
/// </summary>
public sealed class SubscriptionOptions
{
    /// <summary>
    /// The number of events to read and process in each daemon batch.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How many processed events to handle before persisting the subscription checkpoint.
    /// </summary>
    public int CheckpointFrequency { get; set; } = 1;

    /// <summary>
    /// How often to poll for new events when caught up.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum time to hold a distributed lock for a subscription.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum retry attempts before giving up in the daemon loop.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts when processing fails.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}
