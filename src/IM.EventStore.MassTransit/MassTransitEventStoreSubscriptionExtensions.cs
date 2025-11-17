


using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.MassTransit;

public static class MassTransitEventStoreSubscriptionExtensions
{
    public static IEventStoreBuilder<TDbContext> AddMassTransitEventStoreSubscription<TDbContext>(this IEventStoreBuilder<TDbContext> builder, 
        Action<IEventTransformerOptions> configure)
        where TDbContext : DbContext
    {
        builder.Services.AddOptions<EventTransformerOptions>()
            .Configure(configure);

        builder.AddSubscription<MassTransitSubscription>();
        return builder;
    }
}
