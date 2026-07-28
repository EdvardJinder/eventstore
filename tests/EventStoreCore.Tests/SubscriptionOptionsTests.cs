using EventStoreCore;
using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

public class SubscriptionOptionsTests
{
    [Fact]
    public void HasExpectedDefaults()
    {
        var options = new SubscriptionOptions();

        Assert.Equal(8, options.MaxConcurrentWorkers);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(1, options.CheckpointFrequency);
        Assert.Equal(TimeSpan.FromSeconds(10), options.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.LockTimeout);
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.Equal(CheckpointScope.Global, options.CheckpointScope);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RetryDelay);
    }
}
