using Azure.Messaging;
using Azure.Messaging.EventGrid;
using EventStoreCore.CloudEvents;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.EventGrid;

public class EventGridSubscription(IServiceProvider serviceProvider) : ICloudEventSubscription
{
    private readonly IServiceProvider sp = serviceProvider;

    public Task Handle(CloudEvent @event, CancellationToken ct)
    {
        var publisher = sp.GetRequiredService<EventGridPublisherClient>();
        return publisher.SendEventAsync(@event, ct);
    }
}

