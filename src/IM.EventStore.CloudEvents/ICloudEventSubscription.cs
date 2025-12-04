using Azure.Messaging;

namespace IM.EventStore.CloudEvents;

public interface ICloudEventSubscription 
{
     Task Handle(CloudEvent @event, CancellationToken ct);
}
