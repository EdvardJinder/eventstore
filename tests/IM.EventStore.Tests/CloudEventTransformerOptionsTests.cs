using Azure.Messaging;
using IM.EventStore.Abstractions;
using IM.EventStore.CloudEvents;

namespace IM.EventStore.Tests;

public class CloudEventTransformerOptionsTests
{
    private sealed class FakeEvent<T>(Guid id, Guid tenantId, DateTimeOffset timestamp, T data) : IEvent<T> where T : class
    {
        public Guid Id { get; } = id;
        public long Version => 1;
        public object Data => TypedData;
        public new T Data => TypedData;
        public Guid StreamId => Guid.Empty;
        public DateTimeOffset Timestamp { get; } = timestamp;
        public Guid TenantId { get; } = tenantId;
        public Type EventType => typeof(T);
        public T TypedData { get; } = data;
    }

    private sealed class TestEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void MapEvent_StoresCustomCloudEventFactory()
    {
        var options = new CloudEventTransformerOptions();
        var tenantId = Guid.NewGuid();
        var fakeEvent = new FakeEvent<TestEvent>(Guid.NewGuid(), tenantId, DateTimeOffset.UtcNow, new TestEvent { Name = "Custom" });

        options.MapEvent<TestEvent>(ievent => new CloudEvent("source", "type", ievent.Data));

        Assert.True(options._mappings.TryGetValue(typeof(TestEvent), out var mapping));
        var cloudEvent = mapping!(fakeEvent);

        Assert.Equal("type", cloudEvent.Type);
        Assert.Equal("source", cloudEvent.Source.ToString());
        Assert.Equal(fakeEvent.TypedData, cloudEvent.Data);
    }

    [Fact]
    public void MapEvent_BuildsCloudEventWithMetadata()
    {
        var options = new CloudEventTransformerOptions();
        var tenantId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var fakeEvent = new FakeEvent<TestEvent>(Guid.NewGuid(), tenantId, timestamp, new TestEvent { Name = "Metadata" });

        options.MapEvent<TestEvent>("com.im.test", "urn:test", ievent => $"subject/{ievent.Data.Name}");

        var mapping = options._mappings[typeof(TestEvent)];
        var cloudEvent = mapping(fakeEvent);

        Assert.Equal("com.im.test", cloudEvent.Type);
        Assert.Equal("urn:test", cloudEvent.Source.ToString());
        Assert.Equal(timestamp, cloudEvent.Time);
        Assert.Equal($"subject/{fakeEvent.TypedData.Name}", cloudEvent.Subject);
        Assert.Equal(fakeEvent.TypedData, cloudEvent.Data);
        Assert.Equal(tenantId.ToString(), cloudEvent.ExtensionAttributes["tenantid"].ToString());
    }
}
