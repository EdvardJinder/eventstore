using EventStoreCore.CloudEvents;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.EventGrid;

/// <summary>
/// Extension methods for registering Event Grid subscriptions.
/// </summary>
public static class EventGridSubscriptionExtensions
{
    /// <summary>
    /// Adds an Event Grid-backed CloudEvent subscription.
    /// </summary>
    /// <param name="builder">The event store builder.</param>
    /// <param name="configureTransform">Mapping configuration for CloudEvents.</param>
    /// <returns>The event store builder.</returns>
    public static IEventStoreBuilder AddEventGridSubscription(this IEventStoreBuilder builder, Action<CloudEventTransformerOptions> configureTransform)
    {
        builder.AddCloudEventSubscription<EventGridSubscription>(configureTransform);
        return builder;
    }

    /// <summary>
    /// Adds an Event Grid-backed entity-outbox subscription.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureTransform">Mapping configuration for entity-outbox events.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEventGridOutboxSubscription(
        this IServiceCollection services,
        Action<OutboxCloudEventTransformerOptions> configureTransform)
    {
        return services.AddEventGridOutboxSubscription(
            configureTransform,
            _ => { });
    }

    /// <summary>
    /// Adds a configured Event Grid-backed entity-outbox subscription.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureTransform">Mapping configuration for entity-outbox events.</param>
    /// <param name="configureSubscription">Identity, filtering, and failure-policy configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEventGridOutboxSubscription(
        this IServiceCollection services,
        Action<OutboxCloudEventTransformerOptions> configureTransform,
        Action<OutboxSubscriptionRegistrationOptions> configureSubscription)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureTransform);
        ArgumentNullException.ThrowIfNull(configureSubscription);

        services.AddCloudEventOutboxSubscription<EventGridSubscription>(
            configureTransform,
            configureSubscription);
        return services;
    }
}
