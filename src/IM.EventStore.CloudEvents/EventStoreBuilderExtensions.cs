using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IM.EventStore.CloudEvents;

public static class EventStoreBuilderExtensions
{
    public static IEventStoreBuilder<TDbContext> AddCloudEventSubscription<TDbContext, TCloudEventSubscription>(this IEventStoreBuilder<TDbContext> builder, Action<CloudEventTransformerOptions> configureTransformer)
        where TCloudEventSubscription : ICloudEventSubscription
        where TDbContext : DbContext

    {
        builder.Services.TryAddSingleton<CloudEventTransformer>();
        builder.Services.AddOptions<CloudEventTransformerOptions>()
            .Configure(configureTransformer);

        builder.AddSubscription<CloudEventSubscription<TCloudEventSubscription>>();
        return builder;
    }
}
