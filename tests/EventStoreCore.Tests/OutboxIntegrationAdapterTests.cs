using Azure.Messaging;
using EventStoreCore.Abstractions;
using EventStoreCore.CloudEvents;
using EventStoreCore.EventGrid;
using EventStoreCore.MassTransit;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EventStoreCore.Tests;

public sealed class OutboxIntegrationAdapterTests
{
    private sealed record OrderAdded(Guid OrderId);

    private sealed record OrderPublished(Guid OrderId, long OutboxSequence);

    private sealed class RecordingCloudEventSubscription : ICloudEventSubscription
    {
        public List<CloudEvent> Events { get; } = [];

        public Task Handle(CloudEvent @event, CancellationToken ct)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void CloudEventTransformer_ReturnsFalse_WhenNoMappingExists()
    {
        var transformer = new OutboxCloudEventTransformer(
            Options.Create(new OutboxCloudEventTransformerOptions()));

        var transformed = transformer.TryTransform(CreateOutboxEvent(), out var cloudEvent);

        Assert.False(transformed);
        Assert.Null(cloudEvent);
    }

    [Fact]
    public void CloudEventTransformer_AddsStableOutboxMetadata()
    {
        var options = new OutboxCloudEventTransformerOptions();
        options.MapEvent<OrderAdded>(
            "com.example.order-added",
            "urn:orders",
            @event => $"orders/{@event.Data.OrderId}");
        var transformer = new OutboxCloudEventTransformer(Options.Create(options));
        var outboxEvent = CreateOutboxEvent();

        var transformed = transformer.TryTransform(outboxEvent, out var cloudEvent);

        Assert.True(transformed);
        Assert.NotNull(cloudEvent);
        Assert.Equal(outboxEvent.Id.ToString("D"), cloudEvent.Id);
        Assert.Equal(outboxEvent.Timestamp, cloudEvent.Time);
        Assert.Equal($"orders/{outboxEvent.Data.OrderId}", cloudEvent.Subject);
        Assert.Equal(outboxEvent.TenantId.ToString("D"), cloudEvent.ExtensionAttributes["tenantid"]);
        Assert.Equal(outboxEvent.Sequence.ToString(), cloudEvent.ExtensionAttributes["outboxsequence"]);
        Assert.Equal(outboxEvent.SourceEntityType, cloudEvent.ExtensionAttributes["sourceentitytype"]);
        Assert.Equal(outboxEvent.SourceEntityKey, cloudEvent.ExtensionAttributes["sourceentitykey"]);
        Assert.Equal(outboxEvent.ChangeKind.ToString(), cloudEvent.ExtensionAttributes["entitychangekind"]);
    }

    [Fact]
    public void CloudEventTransformer_PreservesExplicitId_WhenConfigured()
    {
        var options = new OutboxCloudEventTransformerOptions();
        options.MapEvent<OrderAdded>(
            @event => new CloudEvent("urn:orders", "com.example.order-added", @event.Data)
            {
                Id = "custom-id"
            },
            preserveCloudEventId: true);
        var transformer = new OutboxCloudEventTransformer(Options.Create(options));

        Assert.True(transformer.TryTransform(CreateOutboxEvent(), out var cloudEvent));
        Assert.Equal("custom-id", cloudEvent.Id);
    }

    [Fact]
    public async Task CloudEventAdapter_RegistersAndPublishesMappedEvents()
    {
        var services = new ServiceCollection();
        var publisher = new RecordingCloudEventSubscription();
        services.AddSingleton(publisher);
        services.AddCloudEventOutboxSubscription<RecordingCloudEventSubscription>(options =>
            options.MapEvent<OrderAdded>(
                @event => new CloudEvent("urn:orders", "com.example.order-added", @event.Data)));
        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetServices<IOutboxSubscription>().Single();

        await subscription.Handle(CreateOutboxEvent(), TestContext.Current.CancellationToken);

        var cloudEvent = Assert.Single(publisher.Events);
        Assert.Equal("com.example.order-added", cloudEvent.Type);
    }

    [Fact]
    public void EventGridAdapter_RegistersAnOutboxSubscription()
    {
        var services = new ServiceCollection();

        services.AddEventGridOutboxSubscription(options =>
            options.MapEvent<OrderAdded>(
                @event => new CloudEvent("urn:orders", "com.example.order-added", @event.Data)));
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IOutboxSubscription>());
    }

    [Fact]
    public void MassTransitOptions_TransformTypedOutboxEvents()
    {
        var options = new OutboxEventTransformerOptions();
        options.AddEvent<OrderAdded, OrderPublished>(
            @event => new OrderPublished(@event.Data.OrderId, @event.Sequence));
        var outboxEvent = CreateOutboxEvent();

        var handler = Assert.Single(options.Handlers[typeof(OrderAdded)]);
        var message = Assert.IsType<OrderPublished>(handler.Transform(outboxEvent));

        Assert.Equal(outboxEvent.Data.OrderId, message.OrderId);
        Assert.Equal(outboxEvent.Sequence, message.OutboxSequence);
    }

    [Fact]
    public async Task MassTransitAdapter_PublishesTransformedEvents()
    {
        var options = new OutboxEventTransformerOptions();
        options.AddEvent<OrderAdded, OrderPublished>(
            @event => new OrderPublished(@event.Data.OrderId, @event.Sequence));
        var bus = Substitute.For<IBus>();
        IPipe<PublishContext>? publishPipe = null;
        bus.Publish(
                Arg.Any<object>(),
                Arg.Any<Type>(),
                Arg.Do<IPipe<PublishContext>>(pipe => publishPipe = pipe),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IOptions<OutboxEventTransformerOptions>>(Options.Create(options));
        services.AddSingleton<MassTransitOutboxSubscription>();
        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetRequiredService<MassTransitOutboxSubscription>();
        var outboxEvent = CreateOutboxEvent();

        await subscription.Handle(outboxEvent, TestContext.Current.CancellationToken);

        await bus.Received(1).Publish(
            Arg.Any<OrderPublished>(),
            typeof(OrderPublished),
            Arg.Any<IPipe<PublishContext>>(),
            Arg.Any<CancellationToken>());

        Assert.NotNull(publishPipe);
        var publishContext = Substitute.For<PublishContext>();
        var headers = Substitute.For<SendHeaders>();
        publishContext.Headers.Returns(headers);
        await publishPipe.Send(publishContext);

        Assert.Equal(outboxEvent.Id, publishContext.MessageId);
        var headerNames = headers.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<string>()
            .ToArray();
        Assert.Contains("EventStore-TenantId", headerNames);
        Assert.Contains("EventStore-OutboxSequence", headerNames);
        Assert.Contains("EventStore-SourceEntityType", headerNames);
        Assert.Contains("EventStore-SourceEntityKey", headerNames);
        Assert.Contains("EventStore-EntityChangeKind", headerNames);
    }

    [Fact]
    public async Task MassTransitAdapter_SkipsUnmappedEvents()
    {
        var bus = Substitute.For<IBus>();
        using var provider = BuildMassTransitProvider(new OutboxEventTransformerOptions(), bus);
        var subscription = provider.GetRequiredService<MassTransitOutboxSubscription>();

        await subscription.Handle(CreateOutboxEvent(), TestContext.Current.CancellationToken);

        await bus.DidNotReceiveWithAnyArgs().Publish(
            Arg.Any<object>(),
            Arg.Any<Type>(),
            Arg.Any<IPipe<PublishContext>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MassTransitAdapter_RejectsNullTransformResults()
    {
        var options = new OutboxEventTransformerOptions();
        options.AddEvent<OrderAdded, OrderPublished>(_ => null!);
        var bus = Substitute.For<IBus>();
        using var provider = BuildMassTransitProvider(options, bus);
        var subscription = provider.GetRequiredService<MassTransitOutboxSubscription>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            subscription.Handle(CreateOutboxEvent(), TestContext.Current.CancellationToken));

        Assert.Contains("Transform returned null", exception.Message);
    }

    [Fact]
    public void MassTransitAdapter_RegistersAnOutboxSubscription()
    {
        var services = new ServiceCollection();

        services.AddMassTransitOutboxSubscription(options =>
            options.AddEvent<OrderAdded, OrderPublished>(
                @event => new OrderPublished(@event.Data.OrderId, @event.Sequence)));
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IOutboxSubscription>());
    }

    private static TestOutboxEvent<OrderAdded> CreateOutboxEvent()
    {
        var orderId = Guid.NewGuid();
        return new TestOutboxEvent<OrderAdded>
        {
            Id = Guid.NewGuid(),
            Sequence = 42,
            Data = new OrderAdded(orderId),
            EventType = typeof(OrderAdded),
            Timestamp = DateTimeOffset.UtcNow,
            TenantId = Guid.NewGuid(),
            SourceEntityType = "Example.Order, Example",
            SourceEntityKey = $$"""{"Id":"{{orderId:D}}"}""",
            ChangeKind = EntityChangeKind.Added
        };
    }

    private static ServiceProvider BuildMassTransitProvider(
        OutboxEventTransformerOptions options,
        IBus bus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IOptions<OutboxEventTransformerOptions>>(Options.Create(options));
        services.AddSingleton<MassTransitOutboxSubscription>();
        return services.BuildServiceProvider();
    }

    private sealed class TestOutboxEvent<T> : IOutboxEvent<T>
        where T : class
    {
        public required Guid Id { get; init; }

        public required long Sequence { get; init; }

        public required T Data { get; init; }

        object IOutboxEvent.Data => Data;

        public required Type EventType { get; init; }

        public required DateTimeOffset Timestamp { get; init; }

        public required Guid TenantId { get; init; }

        public required string SourceEntityType { get; init; }

        public required string SourceEntityKey { get; init; }

        public required EntityChangeKind ChangeKind { get; init; }
    }
}
