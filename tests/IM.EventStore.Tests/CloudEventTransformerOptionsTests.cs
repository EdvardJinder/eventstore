using Azure.Messaging;
using IM.EventStore.Abstractions;
using IM.EventStore.CloudEvents;

namespace IM.EventStore.Tests;

public class CloudEventTransformerOptionsTests
{
   

    private sealed class TestEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void MapEvent_StoresCustomCloudEventFactory()
    {
        var options = new CloudEventTransformerOptions();
        var tenantId = Guid.NewGuid();
        var fakeEvent = new Event<TestEvent>(new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = tenantId,
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = typeof(TestEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"CustomFactory\"}",
        });

        options.MapEvent<TestEvent>(ievent => new CloudEvent("source", "type", ievent.Data));

        Assert.True(options._mappings.TryGetValue(typeof(TestEvent), out var mapping));
        var cloudEvent = mapping!(fakeEvent);

        Assert.Equal("type", cloudEvent.Type);
        Assert.Equal("source", cloudEvent.Source.ToString());
        Assert.Equivalent(fakeEvent.Data, cloudEvent.Data.ToObjectFromJson<TestEvent>());
    }

    [Fact]
    public void MapEvent_BuildsCloudEventWithMetadata()
    {
        var options = new CloudEventTransformerOptions();
        var tenantId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var fakeEvent = new Event<TestEvent>(new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = tenantId,
            Timestamp = timestamp,
            Version = 1,
            Type = typeof(TestEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"CustomFactory\"}",
        });

        options.MapEvent<TestEvent>("com.im.test", "urn:test", ievent => $"subject/{ievent.Data.Name}");

        var mapping = options._mappings[typeof(TestEvent)];
        var cloudEvent = mapping(fakeEvent);

        Assert.Equal("com.im.test", cloudEvent.Type);
        Assert.Equal("urn:test", cloudEvent.Source.ToString());
        Assert.Equal(timestamp, cloudEvent.Time);
        Assert.Equal($"subject/{fakeEvent.Data.Name}", cloudEvent.Subject);
        Assert.Equal(tenantId.ToString(), cloudEvent.ExtensionAttributes["tenantid"].ToString());
    }
}
