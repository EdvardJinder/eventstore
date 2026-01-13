using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace EventStoreCore.Persistence.EntityFrameworkCore.Postgres;

internal sealed class EfCoreEventEventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEfCoreEventStoreBuilder<TDbContext>, IProjectionRegistrar, ISubscriptionDaemonRegistrar, IProjectionDaemonRegistrar
    where TDbContext : DbContext
{
    public IServiceCollection Services => services;

    private readonly List<Action<IServiceProvider, DbContextOptionsBuilder>> dbContextOptionsBuilders = new();

    public void AddProjection<TProjection, TSnapshot>(ProjectionMode mode, Action<IProjectionOptions>? configure) 
        where TProjection : IProjection<TSnapshot>, new() 
        where TSnapshot : class, new()
    {
        var options = new ProjectionOptions();
        configure?.Invoke(options);

        // Get version from attribute or options
        var versionAttr = typeof(TProjection).GetCustomAttributes(typeof(ProjectionVersionAttribute), false)
            .FirstOrDefault() as ProjectionVersionAttribute;
        var version = versionAttr?.Version ?? options.ProjectionVersion;

        var projectionName = typeof(TProjection).FullName ?? typeof(TProjection).Name;

        // Register the projection registration with a factory that captures generic types
        // This avoids reflection at runtime by using compile-time generic constraints
        services.AddSingleton<ProjectionRegistration>(sp => 
            CreateProjectionRegistration<TProjection, TSnapshot>(projectionName, version, options));

        if (mode == ProjectionMode.Inline)
        {
            dbContextOptionsBuilders.Add((sp, dbContextOptionsBuilder) =>
            {
                dbContextOptionsBuilder.AddInterceptors(new ProjectionInterceptor<TProjection, TSnapshot>(options, sp));
            });
        }
        else
        {
            // For eventual mode, register as subscription for backward compatibility
            services.AddSingleton<ISubscription>(sp => new EventualProjectionSubscription<TDbContext, TProjection, TSnapshot>(options, sp));
        }
    }

    internal void ConfigureProjections(IServiceProvider serviceProvider, DbContextOptionsBuilder dbContextOptionsBuilder)
    {
        foreach (var configure in dbContextOptionsBuilders)
        {
            configure(serviceProvider, dbContextOptionsBuilder);
        }
    }

    public void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory)
    {
        services.TryAddSingleton(factory);
        services.TryAddSingleton<SubscriptionDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<SubscriptionDaemon<TDbContext>>());
    }

    public void AddSubscriptionDaemon(Func<IServiceProvider, string> factory)
    {
        AddSubscriptionDaemon(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
    }

    public void AddSubscriptionDaemon(Func<IServiceProvider, DbDataSource> factory)
    {
        AddSubscriptionDaemon(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
    }

    public void AddSubscriptionDaemon(Func<IServiceProvider, IDbConnection> factory)
    {
        AddSubscriptionDaemon(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
    }

    public void AddProjectionDaemon(
        Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory,
        Action<ProjectionDaemonOptions>? configure = null)
    {
        services.TryAddSingleton(lockProviderFactory);
        
        // Register daemon options
        services.Configure<ProjectionDaemonOptions>(opts =>
        {
            configure?.Invoke(opts);
        });

        // Projection registrations are added directly by AddProjection calls

        // Register the daemon
        services.TryAddSingleton<ProjectionDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<ProjectionDaemon<TDbContext>>());

        // Register the projection manager using a factory since the constructor is internal
        services.TryAddScoped<IProjectionManager>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();
            var projections = sp.GetServices<ProjectionRegistration>();
            var logger = sp.GetRequiredService<ILogger<ProjectionManager<TDbContext>>>();
            return new ProjectionManager<TDbContext>(dbContext, lockProvider, projections, logger);
        });
    }

    public void AddProjectionDaemon(
        Func<IServiceProvider, string> connectionStringFactory,
        Action<ProjectionDaemonOptions>? configure = null)
    {
        AddProjectionDaemon(
            sp => new PostgresDistributedSynchronizationProvider(connectionStringFactory(sp)),
            configure);
    }

    public void AddProjectionDaemon(
        Func<IServiceProvider, DbDataSource> dataSourceFactory,
        Action<ProjectionDaemonOptions>? configure = null)
    {
        AddProjectionDaemon(
            sp => new PostgresDistributedSynchronizationProvider(dataSourceFactory(sp)),
            configure);
    }

    /// <summary>
    /// Creates a projection registration using compile-time generic types, avoiding runtime reflection.
    /// </summary>
    private static ProjectionRegistration CreateProjectionRegistration<TProjection, TSnapshot>(
        string projectionName,
        int version, 
        ProjectionOptions options)
        where TProjection : IProjection<TSnapshot>, new()
        where TSnapshot : class, new()
    {
        // Clear action - calls the static ClearAsync method directly (no reflection)
        Func<DbContext, CancellationToken, Task> clearAction = async (db, ct) =>
        {
            var context = new ProjectionContext(db, null!);
            await TProjection.ClearAsync(context, ct);
        };

        // Evolve action - calls the static Evolve method directly (no reflection)
        Func<DbContext, IServiceProvider, object, IEvent, CancellationToken, Task> evolveAction = 
            async (db, serviceProvider, snapshot, @event, ct) =>
            {
                var context = new ProjectionContext(db, serviceProvider);
                await TProjection.Evolve((TSnapshot)snapshot, @event, context, ct);
            };

        // Get-or-create snapshot action - uses generic DbSet<TSnapshot> directly
        Func<DbContext, object, CancellationToken, Task<object>> getOrCreateSnapshotAction = 
            async (db, key, ct) =>
            {
                var dbSet = db.Set<TSnapshot>();
                var snapshot = await dbSet.FindAsync([key], ct);
                return snapshot ?? new TSnapshot();
            };

        // Add snapshot action
        Action<DbContext, object> addSnapshotAction = (db, snapshot) =>
        {
            db.Add((TSnapshot)snapshot);
        };

        return new ProjectionRegistration
        {
            Name = projectionName,
            Version = version,
            ProjectionType = typeof(TProjection),
            SnapshotType = typeof(TSnapshot),
            Options = options,
            ClearAction = clearAction,
            EvolveAction = evolveAction,
            GetOrCreateSnapshotAction = getOrCreateSnapshotAction,
            AddSnapshotAction = addSnapshotAction
        };
    }
}
