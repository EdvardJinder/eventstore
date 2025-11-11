


using Microsoft.EntityFrameworkCore;

namespace IM.EventStore.MassTransit;

public static class MassTransitEventStoreSubscriptionExtensions
{
    public static IEventStoreBuilder<TDbContext> AddMassTransitEventStoreSubscription<TDbContext>(this IEventStoreBuilder<TDbContext> builder)
        where TDbContext : DbContext
    {
        builder.AddSubscription<MassTransitSubscription>();
        return builder;
    }
}
