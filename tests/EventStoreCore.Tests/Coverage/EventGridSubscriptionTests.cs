using Azure;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using EventStoreCore.EventGrid;
using EventStoreCore.CloudEvents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventStoreCore.Tests;

public class EventGridSubscriptionTests
{
    [Fact]
    public void AddEventGridSubscription_RegistersSubscription()
    {
        var services = new ServiceCollection();
        services.AddEventStore(builder => builder.AddEventGridSubscription(_ => { }));

        var provider = services.BuildServiceProvider();

        var subscription = provider.GetRequiredService<CloudEventSubscription<EventGridSubscription>>();

        Assert.NotNull(subscription);
    }

    [Fact]
    public async Task Handle_SendsCloudEventThroughPublisher()
    {
        var services = new ServiceCollection();
        var publisherClient = Substitute.For<EventGridPublisherClient>();
        var response = Substitute.For<Response>();
        var cloudEvent = new CloudEvent("source", "type", new { Name = "Test" });
        var cancellationToken = TestContext.Current.CancellationToken;

        publisherClient
            .SendEventAsync(cloudEvent, cancellationToken)
            .Returns(Task.FromResult(response));

        services.AddSingleton(publisherClient);
        var provider = services.BuildServiceProvider();
        var subscription = new EventGridSubscription(provider);

        await subscription.Handle(cloudEvent, cancellationToken);

        await publisherClient.Received(1).SendEventAsync(cloudEvent, cancellationToken);
    }

}
