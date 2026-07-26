using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore.CloudEvents;

/// <summary>
/// Extension methods for publishing entity-outbox events as CloudEvents.
/// </summary>
public static class CloudEventOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Adds an entity-outbox subscription that maps events to CloudEvents and sends them
    /// through the specified publisher.
    /// </summary>
    /// <typeparam name="TCloudEventSubscription">The CloudEvent publisher implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureTransformer">Mapping configuration for entity-outbox events.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCloudEventOutboxSubscription<TCloudEventSubscription>(
        this IServiceCollection services,
        Action<OutboxCloudEventTransformerOptions> configureTransformer)
        where TCloudEventSubscription : class, ICloudEventSubscription
    {
        return services.AddCloudEventOutboxSubscription<TCloudEventSubscription>(
            configureTransformer,
            _ => { });
    }

    /// <summary>
    /// Adds a configured entity-outbox subscription that maps events to CloudEvents.
    /// </summary>
    /// <typeparam name="TCloudEventSubscription">The CloudEvent publisher implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureTransformer">Mapping configuration for entity-outbox events.</param>
    /// <param name="configureSubscription">Identity, filtering, and failure-policy configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCloudEventOutboxSubscription<TCloudEventSubscription>(
        this IServiceCollection services,
        Action<OutboxCloudEventTransformerOptions> configureTransformer,
        Action<OutboxSubscriptionRegistrationOptions> configureSubscription)
        where TCloudEventSubscription : class, ICloudEventSubscription
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureTransformer);
        ArgumentNullException.ThrowIfNull(configureSubscription);

        services.TryAddSingleton<OutboxCloudEventTransformer>();
        services.AddOptions<OutboxCloudEventTransformerOptions>()
            .Configure(configureTransformer);
        services.TryAddSingleton<TCloudEventSubscription>();
        services.AddOutboxSubscription<CloudEventOutboxSubscription<TCloudEventSubscription>>(
            configureSubscription);
        return services;
    }
}
