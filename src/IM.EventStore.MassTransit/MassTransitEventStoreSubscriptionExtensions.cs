


namespace IM.EventStore.MassTransit;

public static class MassTransitEventStoreSubscriptionExtensions
{
    public static IEventStoreBuilder AddMassTransitEventStoreSubscription(this IEventStoreBuilder builder)
    {
        builder.AddSubscription<MassTransitSubscription>();
        return builder;
    }
}
