using EventStoreCore.Abstractions;

namespace EventStoreCore.CloudEvents;

internal sealed class CloudEventOutboxSubscription<TCloudEventSubscription>(
    OutboxCloudEventTransformer transformer,
    TCloudEventSubscription cloudEventSubscription) : IOutboxSubscription
    where TCloudEventSubscription : class, ICloudEventSubscription
{
    public Task Handle(IOutboxEvent @event, CancellationToken ct)
    {
        return transformer.TryTransform(@event, out var cloudEvent)
            ? cloudEventSubscription.Handle(cloudEvent, ct)
            : Task.CompletedTask;
    }
}
