using Azure.Messaging;

namespace EventStoreCore.CloudEvents;

public interface ICloudEventSubscription 
{
     Task Handle(CloudEvent @event, CancellationToken ct);
}
