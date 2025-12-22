using Azure.Messaging;

namespace EJ.EventStore.CloudEvents;

public interface ICloudEventSubscription 
{
     Task Handle(CloudEvent @event, CancellationToken ct);
}
