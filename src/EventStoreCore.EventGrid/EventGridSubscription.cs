using Azure.Messaging;
using Azure.Messaging.EventGrid;
using EventStoreCore.CloudEvents;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.EventGrid;

public class EventGridSubscription : ICloudEventSubscription
{
    private readonly IServiceProvider sp;
    public Task Handle(CloudEvent @event, CancellationToken ct)
    {
        var publisher = sp.GetRequiredService<EventGridPublisherClient>();
        return publisher.SendEventAsync(@event, ct);
    }
}
