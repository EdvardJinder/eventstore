using IM.EventStore.Abstractions;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;
using System.Data.Common;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace IM.EventStore;

internal sealed class EventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEventStoreBuilder<TDbContext>
    where TDbContext : DbContext
{
    public IServiceCollection Services => services;

    private readonly List<Action<DbContextOptionsBuilder>> dbContextOptionsBuilders = new();
    public IEventStoreBuilder<TDbContext> AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
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

    public IEventStoreBuilder<TDbContext> AddSubscription<TSubscription>() where TSubscription : ISubscription
    {
        services.AddSingleton<Subscription<TSubscription, TDbContext>>();
        services.AddHostedService<Subscription<TSubscription, TDbContext>>(sp => sp.GetRequiredService<Subscription<TSubscription, TDbContext>>());
        return this;
    }

    internal void ConfigureProjections(DbContextOptionsBuilder dbContextOptionsBuilder)
    {
        foreach (var configure in dbContextOptionsBuilders)
        {
            configure(dbContextOptionsBuilder);
        }
    }

    public IEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, string> factory)
    {
        services.TryAddSingleton<IDistributedLockProvider>(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
        return this;
    }

    public IEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, DbDataSource> factory)
    {
        services.TryAddSingleton<IDistributedLockProvider>(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
        return this;
    }

    public IEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, IDbConnection> factory)
    {
        services.TryAddSingleton<IDistributedLockProvider>(sp => new PostgresDistributedSynchronizationProvider(factory(sp)));
        return this;
    }
    public IEventStoreBuilder<TDbContext> AddEventStoreInterfaceToServiceProvider()
    {
        services.AddScoped<IEventStore>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            return new EventStore(dbContext);
        });
        return this;
    }
}
