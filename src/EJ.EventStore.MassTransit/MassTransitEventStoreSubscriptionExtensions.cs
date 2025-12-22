


using Microsoft.Extensions.DependencyInjection;

namespace EJ.EventStore.MassTransit;

public static class MassTransitEventStoreSubscriptionExtensions
{
    public static IEventStoreBuilder AddMassTransitEventStoreSubscription(this IEventStoreBuilder builder, 
        Action<IEventTransformerOptions> configure)
    {
        builder.Services.AddOptions<EventTransformerOptions>()
            .Configure(configure);

        builder.AddSubscription<MassTransitSubscription>();
        return builder;
    }
}
