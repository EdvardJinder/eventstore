using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

public static class EventStoreBuilderEfCoreExtensions
{
    private static (IProjectionRegistrar Projections, ISubscriptionDaemonRegistrar Daemon, IProjectionDaemonRegistrar ProjectionDaemon) GetProvider<TDbContext>(IEventStoreBuilder builder)
        where TDbContext : DbContext
    {
        if (builder.Provider is IProjectionRegistrar proj &&
            builder.Provider is ISubscriptionDaemonRegistrar daemon &&
            builder.Provider is IProjectionDaemonRegistrar projectionDaemon)
        {
            return (proj, daemon, projectionDaemon);
        }
        throw new InvalidOperationException("No EF Core provider is registered. Call ExistingDbContext<TDbContext>() first.");
    }

    public static IEfCoreEventStoreBuilder<TDbContext> ExistingDbContext<TDbContext>(this IEventStoreBuilder builder)
        where TDbContext : DbContext
    {
        var efBuilder = new EfCoreEventEventStoreBuilder<TDbContext>(builder.Services);

        efBuilder.Services.AddDbContext<TDbContext>((sp, options) =>
        {
            efBuilder.ConfigureProjections(sp, options);
        });

        builder.UseProvider(efBuilder);

        return efBuilder;
    }

    public static IEventStoreBuilder AddSubscriptionDaemon<TDbContext>(this IEventStoreBuilder builder)
        where TDbContext : DbContext
    {
        return builder.AddSubscriptionDaemon<TDbContext>(sp => sp.GetRequiredService<IDistributedLockProvider>());
    }

    public static IEventStoreBuilder AddSubscriptionDaemon<TDbContext>(
        this IEventStoreBuilder builder,
        Func<IServiceProvider, IDistributedLockProvider> factory)
        where TDbContext : DbContext
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.Daemon.AddSubscriptionDaemon(factory);
        return builder;
    }

    public static IEventStoreBuilder AddProjectionDaemon<TDbContext>(
        this IEventStoreBuilder builder,
        Action<ProjectionDaemonOptions>? configure = null)
        where TDbContext : DbContext
    {
        return builder.AddProjectionDaemon<TDbContext>(sp => sp.GetRequiredService<IDistributedLockProvider>(), configure);
    }

    public static IEventStoreBuilder AddProjectionDaemon<TDbContext>(
        this IEventStoreBuilder builder,
        Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory,
        Action<ProjectionDaemonOptions>? configure = null)
        where TDbContext : DbContext
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.ProjectionDaemon.AddProjectionDaemon(lockProviderFactory, configure);
        return builder;
    }

    public static IEventStoreBuilder AddProjection<TDbContext, TProjection, TSnapshot>(
        this IEventStoreBuilder builder,
        ProjectionMode mode = ProjectionMode.Inline,
        Action<IProjectionOptions>? configure = null)
        where TDbContext : DbContext
        where TProjection : IProjection<TSnapshot>, new()
        where TSnapshot : class, new()
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.Projections.AddProjection<TProjection, TSnapshot>(mode, configure);
        return builder;
    }
}
