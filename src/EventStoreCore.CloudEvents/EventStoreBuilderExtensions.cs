using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore.CloudEvents;

/// <summary>
/// Extension methods for registering CloudEvents subscriptions.
/// </summary>
public static class EventStoreBuilderExtensions
{
    /// <summary>
    /// Adds a CloudEvents subscription using the specified implementation.
    /// </summary>
    /// <typeparam name="TCloudEventSubscription">The subscription implementation.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="configureTransformer">Mapping configuration for CloudEvents.</param>
    /// <returns>The event store builder.</returns>
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

