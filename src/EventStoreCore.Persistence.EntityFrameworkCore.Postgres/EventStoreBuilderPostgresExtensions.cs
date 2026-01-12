using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading;

namespace EventStoreCore.Persistence.EntityFrameworkCore.Postgres;

public static class EventStoreBuilderPostgresExtensions
{
    private static (IProjectionRegistrar Projections, ISubscriptionDaemonRegistrar Daemon, IProjectionDaemonRegistrar ProjectionDaemon) GetProvider<TDbContext>(IEventStoreBuilder builder)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        if (builder.Provider is IProjectionRegistrar proj && 
            builder.Provider is ISubscriptionDaemonRegistrar daemon &&
            builder.Provider is IProjectionDaemonRegistrar projectionDaemon)
        {
            return (proj, daemon, projectionDaemon);
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

    /// <summary>
    /// Registers the projection daemon background service for processing async projections and handling rebuilds.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="lockProviderFactory">Factory to create the distributed lock provider.</param>
    /// <param name="configure">Optional configuration for the daemon options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IEventStoreBuilder AddProjectionDaemon<TDbContext>(
        this IEventStoreBuilder builder,
        Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory,
        Action<ProjectionDaemonOptions>? configure = null)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.ProjectionDaemon.AddProjectionDaemon(lockProviderFactory, configure);
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
