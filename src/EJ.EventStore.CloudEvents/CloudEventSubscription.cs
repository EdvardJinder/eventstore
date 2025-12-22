using EJ.EventStore.Abstractions;

namespace EJ.EventStore.CloudEvents;

internal class CloudEventSubscription<TCloudEventSubscription> : ISubscription
    where TCloudEventSubscription : ICloudEventSubscription
{
    private readonly CloudEventTransformer _transformer;
    private readonly TCloudEventSubscription _cloudEventSubscription;
    public CloudEventSubscription(CloudEventTransformer transformer, TCloudEventSubscription cloudEventSubscription)
    {
        _transformer = transformer;
        _cloudEventSubscription = cloudEventSubscription;
    }
    public Task Handle(IEvent @event, CancellationToken ct)
    {
        if(_transformer.TryTransform(@event, out var cloudEvent))
        {
            return _cloudEventSubscription.Handle(cloudEvent,  ct);
        }

        return Task.CompletedTask;
    }
}
