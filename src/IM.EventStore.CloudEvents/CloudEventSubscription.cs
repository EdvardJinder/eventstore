using IM.EventStore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.CloudEvents;

public abstract class CloudEventSubscription<TCloudEventSubscription> : ISubscription
    where TCloudEventSubscription : ICloudEventSubscription
{
    public static Task Handle(IEvent @event, IServiceProvider sp, CancellationToken ct)
    {
        var transformer = sp.GetRequiredService<CloudEventTransformer>();
        if(transformer.TryTransform(@event, out var cloudEvent))
        {
            return TCloudEventSubscription.Handle(cloudEvent, sp,  ct);
        }

        return Task.CompletedTask;
    }
}
