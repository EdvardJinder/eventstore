using Azure.Messaging;
using Azure.Messaging.EventGrid;
using IM.EventStore.CloudEvents;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.EventGrid;

public class EventGridSubscription : ICloudEventSubscription
{
    private readonly IServiceProvider sp;
    public Task Handle(CloudEvent @event, CancellationToken ct)
    {
        var publisher = sp.GetRequiredService<EventGridPublisherClient>();
        return publisher.SendEventAsync(@event, ct);
    }
}
