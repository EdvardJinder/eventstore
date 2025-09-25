using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

internal sealed class EventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEventStoreBuilder
    where TDbContext : DbContext
{
    public IEventStoreBuilder AddSubscription<TSubscription>()
        where TSubscription : ISubscription
    {
        services.AddScoped(typeof(TSubscription));
        services.AddScoped<ISubscription>(sp => sp.GetRequiredService<TSubscription>());
        return this;
    }
    public IEventStoreBuilder AddProjection<TProjection, TSnapshot>()
        where TProjection : IProjection<TSnapshot>
        where TSnapshot : class, new()
    {
        services.AddScoped(typeof(TProjection));
        services.AddScoped<ProjectionWrapper<TProjection, TSnapshot, TDbContext>>();
        services.AddScoped<ISubscription>(sp => sp.GetRequiredService<ProjectionWrapper<TProjection, TSnapshot, TDbContext>>());
        return this;
    }
}

