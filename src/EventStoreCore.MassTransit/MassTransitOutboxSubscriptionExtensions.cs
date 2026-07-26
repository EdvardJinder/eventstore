using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.MassTransit;

/// <summary>
/// Extension methods for publishing entity-outbox events through MassTransit.
/// </summary>
public static class MassTransitOutboxSubscriptionExtensions
{
    /// <summary>
    /// Adds a MassTransit-backed entity-outbox subscription with event transformations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Transformation configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMassTransitOutboxSubscription(
        this IServiceCollection services,
        Action<IOutboxEventTransformerOptions> configure)
    {
        return services.AddMassTransitOutboxSubscription(configure, _ => { });
    }

    /// <summary>
    /// Adds a configured MassTransit-backed entity-outbox subscription.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Transformation configuration.</param>
    /// <param name="configureSubscription">Identity, filtering, and failure-policy configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMassTransitOutboxSubscription(
        this IServiceCollection services,
        Action<IOutboxEventTransformerOptions> configure,
        Action<OutboxSubscriptionRegistrationOptions> configureSubscription)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(configureSubscription);

        services.AddOptions<OutboxEventTransformerOptions>()
            .Configure(configure);
        services.AddOutboxSubscription<MassTransitOutboxSubscription>(
            configureSubscription);
        return services;
    }
}
