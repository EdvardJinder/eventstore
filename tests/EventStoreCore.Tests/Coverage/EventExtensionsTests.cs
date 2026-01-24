using EventStoreCore;

namespace EventStoreCore.Tests;

public class EventExtensionsTests
{
    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void ToEvent_Throws_WhenEventTypeCannotBeLoaded()
    {
        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = "Missing.Assembly.Type",
            Data = "{}"
        };

        var exception = Assert.Throws<EventMaterializationException>(() => dbEvent.ToEvent());

        Assert.Contains("Could not resolve event type", exception.Message);
    }

    [Fact]
    public void ToEvent_Throws_WhenEventPayloadCannotBeDeserialized()
    {
        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            Data = "null"
        };

        var exception = Assert.Throws<EventMaterializationException>(() => dbEvent.ToEvent());

        Assert.Contains("Could not deserialize event data", exception.Message);
    }
}
