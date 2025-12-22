using EJ.EventStore.Abstractions;
using EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading;

namespace EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;

public static class EventStoreBuilderPostgresExtensions
{
    private static (IProjectionRegistrar Projections, ISubscriptionDaemonRegistrar Daemon) GetProvider<TDbContext>(IEventStoreBuilder builder)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        if (builder.Provider is IProjectionRegistrar proj && builder.Provider is ISubscriptionDaemonRegistrar daemon)
        {
            return (proj, daemon);
        }
        throw new InvalidOperationException("No EF Core provider is registered. Call ExistingDbContext<TDbContext>() first.");
    }

    public static IEventStoreBuilder AddSubscriptionDaemon<TDbContext>(this IEventStoreBuilder builder, Func<IServiceProvider, IDistributedLockProvider> factory)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.Daemon.AddSubscriptionDaemon(factory);
        return builder;
    }

    public static IEventStoreBuilder AddProjection<TDbContext, TProjection, TSnapshot>(this IEventStoreBuilder builder, ProjectionMode mode = ProjectionMode.Inline, Action<IProjectionOptions>? configure = null)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
        where TProjection : IProjection<TSnapshot>, new()
        where TSnapshot : class, new()
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.Projections.AddProjection<TProjection, TSnapshot>(mode, configure);
        return builder;
    }
}
