using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

public class SubscriptionStatusDtoCoverageTests
{
    [Fact]
    public void RecordProperties_AreAccessible()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var dto = new SubscriptionStatusDto("sub", 2, 10, 20.5, timestamp);

        Assert.Equal("sub", dto.SubscriptionName);
        Assert.Equal(2, dto.Position);
        Assert.Equal(10, dto.TotalEvents);
        Assert.Equal(20.5, dto.ProgressPercentage);
        Assert.Equal(timestamp, dto.LastProcessedAt);
    }
}
