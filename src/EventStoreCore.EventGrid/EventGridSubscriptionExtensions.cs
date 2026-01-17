using EventStoreCore.CloudEvents;

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
}
