using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.MassTransit;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EventStoreCore.Tests;

public class MassTransitSubscriptionCoverageTests
{
    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SampleMessage
    {
        public string Name { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildProvider(EventTransformerOptions options, IBus bus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IOptions<EventTransformerOptions>>(Options.Create(options));
        services.AddSingleton<MassTransitSubscription>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_SkipsWhenNoHandler()
    {
        var transformerOptions = new EventTransformerOptions();
        var bus = Substitute.For<IBus>();
        var provider = BuildProvider(transformerOptions, bus);
        var subscription = provider.GetRequiredService<MassTransitSubscription>();

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

        await subscription.Handle(@event, TestContext.Current.CancellationToken);

        await bus.DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LogsAndSkipsWhenTransformReturnsNull()
    {
        var transformerOptions = new EventTransformerOptions();
        transformerOptions.AddEvent<SampleEvent, SampleMessage>(_ => null!);
        var bus = Substitute.For<IBus>();
        var provider = BuildProvider(transformerOptions, bus);
        var subscription = provider.GetRequiredService<MassTransitSubscription>();

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

        await subscription.Handle(@event, TestContext.Current.CancellationToken);

        await bus.DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PublishesTransformedEvent()
    {
        var transformerOptions = new EventTransformerOptions();
        transformerOptions.AddEvent<SampleEvent, SampleMessage>(e => new SampleMessage { Name = e.Data.Name });
        var bus = Substitute.For<IBus>();
        var provider = BuildProvider(transformerOptions, bus);
        var subscription = provider.GetRequiredService<MassTransitSubscription>();

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

        await subscription.Handle(@event, TestContext.Current.CancellationToken);

        await bus.Received(1)
            .Publish(Arg.Any<SampleMessage>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }
}
