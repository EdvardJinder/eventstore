using EJ.EventStore;

namespace EJ.EventStore.Tests;

public class SubscriptionOptionsTests
{
    [Fact]
    public void HasExpectedDefaults()
    {
        var options = new SubscriptionOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), options.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.LockTimeout);
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RetryDelay);
    }
}
