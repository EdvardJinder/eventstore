using EventStoreCore;

namespace EventStoreCore.Tests;

public class EventExtensionsTests
{
    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ConflictingEvent
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

    [Fact]
    public void ToEvent_Throws_WhenTypeNameRegistrationConflicts()
    {
        var registry = new EventTypeRegistry(new[]
        {
            new EventTypeRegistration(typeof(ConflictingEvent), "sample_event")
        });

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            TypeName = "sample_event",
            Data = "{\"Name\":\"Test\"}"
        };

        var exception = Assert.Throws<EventMaterializationException>(() => dbEvent.ToEvent(registry));

        Assert.Contains("registered for", exception.Message);
    }

    [Fact]
    public void ToEvent_Throws_WhenTypeIsMissing()
    {
        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = " ",
            Data = "{}"
        };

        var exception = Assert.Throws<EventMaterializationException>(() => dbEvent.ToEvent());

        Assert.Contains("Event type is required", exception.Message);
    }
}
