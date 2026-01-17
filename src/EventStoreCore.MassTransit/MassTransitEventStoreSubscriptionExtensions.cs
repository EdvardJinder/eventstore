


using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.MassTransit;

/// <summary>
/// Extension methods for registering MassTransit subscriptions.
/// </summary>
public static class MassTransitEventStoreSubscriptionExtensions
{
    /// <summary>
    /// Adds a MassTransit-backed subscription with event transformations.
    /// </summary>
    /// <param name="builder">The event store builder.</param>
    /// <param name="configure">Transformation configuration.</param>
    /// <returns>The event store builder.</returns>
    public static IEventStoreBuilder AddMassTransitEventStoreSubscription(this IEventStoreBuilder builder, 
        Action<IEventTransformerOptions> configure)
    {
        builder.Services.AddOptions<EventTransformerOptions>()
            .Configure(configure);

        builder.AddSubscription<MassTransitSubscription>();
        return builder;
    }
}

