using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;
using System.Data.Common;

namespace IM.EventStore;

internal sealed class EventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEventStoreBuilder
    where TDbContext : DbContext
{
    IServiceCollection IEventStoreBuilder.Services => services;

    private readonly List<Action<DbContextOptionsBuilder>> dbContextOptionsBuilders = new();
    public IEventStoreBuilder AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
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

    public IEventStoreBuilder AddSubscriptionDaemon(string connectionString)
    {
        services.TryAddSingleton<IDistributedLockProvider>(new PostgresDistributedSynchronizationProvider(connectionString));
        return this;
    }
    public IEventStoreBuilder AddSubscriptionDaemon(DbDataSource dbDataSource)
    {
        services.TryAddSingleton<IDistributedLockProvider>(new PostgresDistributedSynchronizationProvider(dbDataSource));
        return this;
    }
    public IEventStoreBuilder AddSubscriptionDaemon(IDbConnection dbConnection)
    {
        services.TryAddSingleton<IDistributedLockProvider>(new PostgresDistributedSynchronizationProvider(dbConnection));
        return this;
    }
    public IEventStoreBuilder AddSubscription<TSubscription>() where TSubscription : ISubscription
    {

        services.AddSingleton<Subscription<TSubscription, TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<Subscription<TSubscription, TDbContext>>());
        return this;
    }

    internal void ConfigureProjections(DbContextOptionsBuilder dbContextOptionsBuilder)
    {
        foreach (var configure in dbContextOptionsBuilders)
        {
            configure(dbContextOptionsBuilder);
        }
    }

}
