using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EventStoreCore;

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

        var versionAttr = typeof(TProjection).GetCustomAttributes(typeof(ProjectionVersionAttribute), false)
            .FirstOrDefault() as ProjectionVersionAttribute;
        var version = versionAttr?.Version ?? options.ProjectionVersion;

        var projectionName = typeof(TProjection).FullName ?? typeof(TProjection).Name;

        services.AddSingleton<ProjectionRegistration>(sp =>
            CreateProjectionRegistration<TProjection, TSnapshot>(projectionName, mode, version, options));

        if (mode == ProjectionMode.Inline)
        {
            dbContextOptionsBuilders.Add((sp, dbContextOptionsBuilder) =>
            {
                dbContextOptionsBuilder.AddInterceptors(new ProjectionInterceptor<TProjection, TSnapshot>(options, sp, version));
            });
        }
        else
        {
            services.AddSingleton<ISubscription>(_ => new EventualProjectionSubscription<TDbContext, TProjection, TSnapshot>(options));
        }
    }

    internal void ConfigureProjections(IServiceProvider serviceProvider, DbContextOptionsBuilder dbContextOptionsBuilder)
    {
        foreach (var configure in dbContextOptionsBuilders)
        {
            configure(serviceProvider, dbContextOptionsBuilder);
        }
    }

    public void AddSubscriptionDaemon(
        Func<IServiceProvider, IDistributedLockProvider> factory,
        Action<SubscriptionOptions>? configure = null)
    {
        services.TryAddSingleton(factory);
        services.AddOptions<SubscriptionOptions>()
            .Configure(opts => configure?.Invoke(opts))
            .Validate(
                opts => opts.LockTimeout >= TimeSpan.Zero || opts.LockTimeout == Timeout.InfiniteTimeSpan,
                $"{nameof(SubscriptionOptions.LockTimeout)} must be non-negative or Timeout.InfiniteTimeSpan.")
            .ValidateOnStart();
        services.TryAddSingleton<SubscriptionDaemon<TDbContext>>();
        services.TryAddScoped<ISubscriptionManager>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();
            var subscriptions = sp.GetServices<ISubscription>();
            var logger = sp.GetRequiredService<ILogger<SubscriptionManager<TDbContext>>>();
            return new SubscriptionManager<TDbContext>(dbContext, lockProvider, subscriptions, logger);
        });
        services.AddHostedService(sp => sp.GetRequiredService<SubscriptionDaemon<TDbContext>>());
    }


    public void AddProjectionDaemon(
        Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory,
        Action<ProjectionDaemonOptions>? configure = null)
    {
        services.TryAddSingleton(lockProviderFactory);

        services.AddOptions<ProjectionDaemonOptions>()
            .Configure(opts => configure?.Invoke(opts))
            .Validate(
                opts => opts.LockTimeout >= TimeSpan.Zero || opts.LockTimeout == Timeout.InfiniteTimeSpan,
                $"{nameof(ProjectionDaemonOptions.LockTimeout)} must be non-negative or Timeout.InfiniteTimeSpan.")
            .ValidateOnStart();

        services.TryAddSingleton<ProjectionDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<ProjectionDaemon<TDbContext>>());

        services.TryAddScoped<IProjectionManager>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();
            var projections = sp.GetServices<ProjectionRegistration>();
            var logger = sp.GetRequiredService<ILogger<ProjectionManager<TDbContext>>>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProjectionDaemonOptions>>();
            return new ProjectionManager<TDbContext>(dbContext, lockProvider, projections, logger, sp, options);
        });
    }

    private static ProjectionRegistration CreateProjectionRegistration<TProjection, TSnapshot>(
        string projectionName,
        ProjectionMode mode,
        int version,
        ProjectionOptions options)
        where TProjection : IProjection<TSnapshot>, new()
        where TSnapshot : class, new()
    {
        Func<DbContext, IServiceProvider, CancellationToken, Task> clearAction = async (db, serviceProvider, ct) =>
        {
            var context = new ProjectionContext(db, serviceProvider);
            await TProjection.ClearAsync(context, ct);
        };

        Func<DbContext, IServiceProvider, object, IEvent, CancellationToken, Task> evolveAction =
            async (db, serviceProvider, snapshot, @event, ct) =>
            {
                var context = new ProjectionContext(db, serviceProvider);
                await TProjection.Evolve((TSnapshot)snapshot, @event, context, ct);
            };

        Func<DbContext, object, CancellationToken, Task<object>> getOrCreateSnapshotAction =
            async (db, key, ct) =>
            {
                var dbSet = db.Set<TSnapshot>();
                var snapshot = await dbSet.FindAsync([key], ct);
                return snapshot ?? new TSnapshot();
            };

        Action<DbContext, object> addSnapshotAction = (db, snapshot) =>
        {
            db.Add((TSnapshot)snapshot);
        };

        return new ProjectionRegistration
        {
            Name = projectionName,
            Mode = mode,
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
