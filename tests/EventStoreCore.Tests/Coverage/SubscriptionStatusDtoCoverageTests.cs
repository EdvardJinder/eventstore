using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

public class SubscriptionStatusDtoCoverageTests
{
    [Fact]
    public void RecordProperties_AreAccessible()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var nextAttemptAt = timestamp.AddMinutes(1);
        var dto = new SubscriptionStatusDto(
            "sub",
            2,
            SubscriptionState.Faulted,
            10,
            20.5,
            timestamp,
            "boom",
            3,
            timestamp,
            nextAttemptAt,
            4);

        Assert.Equal("sub", dto.SubscriptionName);
        Assert.Equal(2, dto.Position);
        Assert.Equal(SubscriptionState.Faulted, dto.State);
        Assert.Equal(10, dto.TotalEvents);
        Assert.Equal(20.5, dto.ProgressPercentage);
        Assert.Equal(timestamp, dto.LastProcessedAt);
        Assert.Equal("boom", dto.LastError);
        Assert.Equal(3, dto.AttemptCount);
        Assert.Equal(timestamp, dto.LastAttemptAt);
        Assert.Equal(nextAttemptAt, dto.NextAttemptAt);
        Assert.Equal(4, dto.FailedEventSequence);
    }
}
