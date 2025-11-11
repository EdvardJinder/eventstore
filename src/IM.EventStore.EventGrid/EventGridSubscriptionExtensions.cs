using IM.EventStore.CloudEvents;
using Microsoft.EntityFrameworkCore;

namespace IM.EventStore.EventGrid;

public static class EventGridSubscriptionExtensions
{
    public static IEventStoreBuilder<TDbContext> AddEventGridSubscription<TDbContext>(this IEventStoreBuilder<TDbContext> builder, Action<CloudEventTransformerOptions> configureTransform)
        where TDbContext : DbContext
    {
        builder.AddCloudEventSubscription<TDbContext, EventGridSubscription>(configureTransform);
        return builder;
    }
}