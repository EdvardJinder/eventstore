using EventStoreCore;
using Microsoft.Extensions.DependencyInjection;

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

    private sealed class CurrentEvent
    {
        public string Name { get; set; } = string.Empty;

        public string Currency { get; set; } = string.Empty;
    }

    private sealed class OldEvent
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
    public void ToEvent_UsesRegisteredTypeName_WhenClrTypeDiffers()
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

        var @event = dbEvent.ToEvent(registry);

        Assert.IsType<ConflictingEvent>(@event.Data);
        Assert.Equal(typeof(ConflictingEvent), @event.EventType);
    }

    [Fact]
    public void ToEvent_UsesLogicalTypeName_WhenClrTypeCannotBeLoaded()
    {
        var registry = new EventTypeRegistry(new[]
        {
            new EventTypeRegistration(typeof(SampleEvent), "sample_event")
        });

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = "Missing.Assembly.Type",
            TypeName = "sample_event",
            Data = "{\"Name\":\"Test\"}"
        };

        var @event = dbEvent.ToEvent(registry);

        var data = Assert.IsType<SampleEvent>(@event.Data);
        Assert.Equal("Test", data.Name);
        Assert.Equal(typeof(SampleEvent), @event.EventType);
    }

    [Fact]
    public void ToEvent_UsesAlias_WhenStoredTypeNameIsOldName()
    {
        var registry = new EventTypeRegistry(
            new[] { new EventTypeRegistration(typeof(SampleEvent), "sample_event") },
            new[] { new EventTypeAliasRegistration(typeof(SampleEvent), "old_sample_event") },
            []);

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = "Missing.Assembly.Type",
            TypeName = "old_sample_event",
            Data = "{\"Name\":\"Test\"}"
        };

        var @event = dbEvent.ToEvent(registry);

        var data = Assert.IsType<SampleEvent>(@event.Data);
        Assert.Equal("Test", data.Name);
    }

    [Fact]
    public void ToEvent_AppliesTypedUpcaster()
    {
        var registry = BuildRegistry(c => c.AddEvent<CurrentEvent>("current_event", e => e
            .AddUpcaster<OldEvent>("old_event", old => new CurrentEvent
            {
                Name = old.Name,
                Currency = "USD"
            })));

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = "Missing.Assembly.Type",
            TypeName = "old_event",
            Data = "{\"Name\":\"Test\"}"
        };

        var @event = dbEvent.ToEvent(registry);

        var data = Assert.IsType<CurrentEvent>(@event.Data);
        Assert.Equal("Test", data.Name);
        Assert.Equal("USD", data.Currency);
    }

    [Fact]
    public void ToEvent_AppliesJsonUpcaster()
    {
        var registry = BuildRegistry(c => c.AddEvent<CurrentEvent>("current_event", e => e
            .AddUpcaster("legacy_event", json => new CurrentEvent
            {
                Name = json["legacyName"]!.GetValue<string>(),
                Currency = "EUR"
            })));

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Type = "Missing.Assembly.Type",
            TypeName = "legacy_event",
            Data = "{\"legacyName\":\"Test\"}"
        };

        var @event = dbEvent.ToEvent(registry);

        var data = Assert.IsType<CurrentEvent>(@event.Data);
        Assert.Equal("Test", data.Name);
        Assert.Equal("EUR", data.Currency);
    }

    [Fact]
    public void EventTypeRegistry_Throws_WhenUpcasterSourceIsRegisteredTwice()
    {
        var services = new ServiceCollection();
        services.AddEventStore(c => c.AddEvent<CurrentEvent>("current_event", e => e
            .AddUpcaster<OldEvent>("old_event", old => new CurrentEvent { Name = old.Name })
            .AddUpcaster<OldEvent>("old_event", old => new CurrentEvent { Name = old.Name })));

        var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<EventTypeRegistry>());

        Assert.Contains("already registered", exception.Message);
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

    private static EventTypeRegistry BuildRegistry(Action<IEventStoreBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddEventStore(configure);
        return services.BuildServiceProvider().GetRequiredService<EventTypeRegistry>();
    }
}
