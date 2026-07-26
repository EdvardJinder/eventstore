using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Service collection extensions for registering event store services.
/// </summary>
public static class EventStoreExtensions
{
    /// <summary>
    /// Adds the event store services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional builder configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEventStore(
       this IServiceCollection services,
       Action<IEventStoreBuilder>? configure = null
           )
    {
        services.TryAddSingleton(sp => new EventTypeRegistry(
            sp.GetServices<EventTypeRegistration>(),
            sp.GetServices<EventTypeAliasRegistration>(),
            sp.GetServices<EventUpcasterRegistration>(),
            sp.GetServices<EventSchemaUpcasterRegistration>()));
        services.TryAddSingleton<SnapshotRegistry>();
        services.TryAddSingleton<IEventStoreSerializer, SystemTextJsonEventStoreSerializer>();

        EventStoreBuilder builder = new EventStoreBuilder(services);

        configure?.Invoke(builder);

        return services;
    }
}


