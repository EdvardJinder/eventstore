using IM.EventStore.Abstractions;
using IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;
using System.Data.Common;

namespace IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;


internal sealed class EfCoreEventEventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEfCoreEventStoreBuilder<TDbContext>
    where TDbContext : DbContext
{
    public IServiceCollection Services => services;

    private readonly List<Action<DbContextOptionsBuilder>> dbContextOptionsBuilders = new();
    public IEfCoreEventStoreBuilder<TDbContext> AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
        where TProjection : IProjection<TSnapshot>, new()
        where TSnapshot : class, new()
    {
        var options = new ProjectionOptions();
        configure?.Invoke(options);

        dbContextOptionsBuilders.Add(dbContextOptionsBuilder =>
        {
            dbContextOptionsBuilder.AddInterceptors(new ProjectionInterceptor<TProjection, TSnapshot>(options));
        });

        return this;
    }

    internal void ConfigureProjections(DbContextOptionsBuilder dbContextOptionsBuilder)
    {
        foreach (var configure in dbContextOptionsBuilders)
        {
            configure(dbContextOptionsBuilder);
        }
    }

    public IEfCoreEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, string> factory)
    {
        services.TryAddSingleton<IDistributedLockProvider>(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
        services.TryAddSingleton<SubscriptionDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<SubscriptionDaemon<TDbContext>>());
        return this;
    }

    public IEfCoreEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, DbDataSource> factory)
    {
        services.TryAddSingleton<IDistributedLockProvider>(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
        services.TryAddSingleton<SubscriptionDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<SubscriptionDaemon<TDbContext>>());
        return this;
    }

    public IEfCoreEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, IDbConnection> factory)
    {
        services.TryAddSingleton<IDistributedLockProvider>(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
        services.TryAddSingleton<SubscriptionDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<SubscriptionDaemon<TDbContext>>());
        return this;
    }
    
}
