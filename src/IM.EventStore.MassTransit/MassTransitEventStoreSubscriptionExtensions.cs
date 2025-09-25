

namespace IM.EventStore.MassTransit;

public static class MassTransitEventStoreSubscriptionExtensions
{
    extension(IEventStoreBuilder builder)
    {
        public IEventStoreBuilder AddMassTransitEventStoreSubscription()
        {
            builder.AddSubscription<MassTransitEventStoreSubscription>();
            return builder;
        }
    }
}
