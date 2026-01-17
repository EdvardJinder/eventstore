using Azure.Messaging;
using EventStoreCore;
using EventStoreCore.CloudEvents;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Tests;

public class CloudEventTransformerCoverageTests
{
    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void TryTransform_ReturnsFalse_WhenNoMappingExists()
    {
        var options = Options.Create(new CloudEventTransformerOptions());
        var transformer = new CloudEventTransformer(options);

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Test\"}"
        };
        var @event = new Event<SampleEvent>(dbEvent);

        var result = transformer.TryTransform(@event, out var cloudEvent);

        Assert.False(result);
        Assert.Null(cloudEvent);
    }

    [Fact]
    public void TryTransform_ReturnsTrue_WhenMappingExists()
    {
        var transformerOptions = new CloudEventTransformerOptions();
        transformerOptions.MapEvent<SampleEvent>(e => new CloudEvent("source", "type", e.Data));
        var options = Options.Create(transformerOptions);
        var transformer = new CloudEventTransformer(options);

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Test\"}"
        };
        var @event = new Event<SampleEvent>(dbEvent);

        var result = transformer.TryTransform(@event, out var cloudEvent);

        Assert.True(result);
        Assert.NotNull(cloudEvent);
        Assert.Equal("type", cloudEvent.Type);
    }
}
