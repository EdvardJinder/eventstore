using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

internal sealed class EventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEventStoreBuilder
    where TDbContext : DbContext
{
    public IEventStoreBuilder AddSubscription<TSubscription>(Action<ISubscriptionOptions>? configure = null)
        where TSubscription : ISubscription
    {

        services.AddOptions<SubscriptionOptions>(typeof(TSubscription).AssemblyQualifiedName)
            .Configure(options =>
            {
                configure?.Invoke(options);
            });

        services.AddScoped(typeof(TSubscription));
        services.AddScoped<ISubscription>(sp => sp.GetRequiredService<TSubscription>());
        return this;
    }
    public IEventStoreBuilder AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
        where TProjection : IProjection<TSnapshot>
        where TSnapshot : class, new()
    {

        services.AddOptions<SubscriptionOptions>(typeof(ProjectionWrapper<TProjection, TSnapshot, TDbContext>).AssemblyQualifiedName)
            .Configure(options =>
            {
                configure?.Invoke(options);
            });

        services.AddScoped(typeof(TProjection));
        services.AddScoped<ProjectionWrapper<TProjection, TSnapshot, TDbContext>>();
        services.AddScoped<ISubscription>(sp => sp.GetRequiredService<ProjectionWrapper<TProjection, TSnapshot, TDbContext>>());

        return this;
    }

    
}

