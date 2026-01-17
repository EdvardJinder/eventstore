using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

/// <summary>
/// Extension methods for configuring the EF Core event store provider.
/// </summary>
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

    /// <summary>
    /// Registers an existing DbContext and enables event store integrations.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <returns>The EF Core event store builder.</returns>
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

    /// <summary>
    /// Adds the subscription daemon using the default distributed lock provider.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <returns>The event store builder.</returns>
    public static IEventStoreBuilder AddSubscriptionDaemon<TDbContext>(this IEventStoreBuilder builder)
        where TDbContext : DbContext
    {
        return builder.AddSubscriptionDaemon<TDbContext>(sp => sp.GetRequiredService<IDistributedLockProvider>());
    }

    /// <summary>
    /// Adds the subscription daemon using a custom distributed lock provider.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="factory">Factory that returns the distributed lock provider.</param>
    /// <returns>The event store builder.</returns>
    public static IEventStoreBuilder AddSubscriptionDaemon<TDbContext>(
        this IEventStoreBuilder builder,
        Func<IServiceProvider, IDistributedLockProvider> factory)
        where TDbContext : DbContext
    {
        var provider = GetProvider<TDbContext>(builder);
        provider.Daemon.AddSubscriptionDaemon(factory);
        return builder;
    }

    /// <summary>
    /// Adds the projection daemon using the default distributed lock provider.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="configure">Optional daemon configuration.</param>
    /// <returns>The event store builder.</returns>
    public static IEventStoreBuilder AddProjectionDaemon<TDbContext>(
        this IEventStoreBuilder builder,
        Action<ProjectionDaemonOptions>? configure = null)
        where TDbContext : DbContext
    {
        return builder.AddProjectionDaemon<TDbContext>(sp => sp.GetRequiredService<IDistributedLockProvider>(), configure);
    }

    /// <summary>
    /// Adds the projection daemon using a custom distributed lock provider.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="lockProviderFactory">Factory that returns the distributed lock provider.</param>
    /// <param name="configure">Optional daemon configuration.</param>
    /// <returns>The event store builder.</returns>
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

    /// <summary>
    /// Registers a projection for the specified DbContext.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    /// <typeparam name="TProjection">The projection implementation.</typeparam>
    /// <typeparam name="TSnapshot">The snapshot entity type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="mode">Whether to run inline or eventual projections.</param>
    /// <param name="configure">Optional projection options configuration.</param>
    /// <returns>The event store builder.</returns>
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

