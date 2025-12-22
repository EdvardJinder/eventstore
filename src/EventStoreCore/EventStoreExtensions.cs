using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

public static class EventStoreExtensions
{
    
    public static IServiceCollection AddEventStore(
       this IServiceCollection services,
       Action<IEventStoreBuilder>? configure = null
           )
    {

        EventStoreBuilder builder = new EventStoreBuilder(services);

        configure?.Invoke(builder);

              return services;
    }

    
}

