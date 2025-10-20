using Azure.Messaging;

namespace IM.EventStore.CloudEvents;

public interface ICloudEventSubscription 
{
    static abstract Task Handle(CloudEvent @event, IServiceProvider sp, CancellationToken ct);
}
