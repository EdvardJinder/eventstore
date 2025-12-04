using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IM.EventStore.CloudEvents;

public static class EventStoreBuilderExtensions
{
    public static IEventStoreBuilder AddCloudEventSubscription<TCloudEventSubscription>(this IEventStoreBuilder builder, Action<CloudEventTransformerOptions> configureTransformer)
        where TCloudEventSubscription : ICloudEventSubscription
    {
        builder.Services.TryAddSingleton<CloudEventTransformer>();
        builder.Services.AddOptions<CloudEventTransformerOptions>()
            .Configure(configureTransformer);
        builder.Services.TryAddSingleton(typeof(TCloudEventSubscription));
        builder.AddSubscription<CloudEventSubscription<TCloudEventSubscription>>();
        return builder;
    }
}
