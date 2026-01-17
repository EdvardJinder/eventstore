using EventStoreCore.Abstractions;
using EventStoreCore.MassTransit;

namespace EventStoreCore.Tests;

public class EventTransformerOptionsCoverageTests
{
    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SampleMessage
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void AddEvent_StoresTransformAndOutputType()
    {
        var options = new EventTransformerOptions();

        options.AddEvent<SampleEvent, SampleMessage>(e => new SampleMessage { Name = e.Data.Name });

        Assert.True(options.Handlers.TryGetValue(typeof(SampleEvent), out var handler));
        Assert.Equal(typeof(SampleMessage), handler.Out);
        Assert.NotNull(handler.Transform);

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

        var message = handler.Transform(@event) as SampleMessage;

        Assert.NotNull(message);
        Assert.Equal("Test", message!.Name);
    }
}
