using IM.EventStore.CloudEvents;

namespace IM.EventStore.EventGrid;

public static class EventGridSubscriptionExtensions
{
    public static IEventStoreBuilder AddEventGridSubscription(this IEventStoreBuilder builder, Action<CloudEventTransformerOptions> configureTransform)
    {
        builder.AddCloudEventSubscription<EventGridSubscription>(configureTransform);
        return builder;
    }
}