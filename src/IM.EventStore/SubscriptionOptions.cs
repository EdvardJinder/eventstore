public sealed class SubscriptionOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}