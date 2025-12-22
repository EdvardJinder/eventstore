using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;
using System.Data.Common;

namespace EventStoreCore.Persistence.EntityFrameworkCore.Postgres;

internal sealed class EfCoreEventEventStoreBuilder<TDbContext>(
    IServiceCollection services
    ) : IEfCoreEventStoreBuilder<TDbContext>, IProjectionRegistrar, ISubscriptionDaemonRegistrar
    where TDbContext : DbContext
{
    public IServiceCollection Services => services;

    private readonly List<Action<IServiceProvider, DbContextOptionsBuilder>> dbContextOptionsBuilders = new();

    public void AddProjection<TProjection, TSnapshot>(ProjectionMode mode, Action<IProjectionOptions>? configure) where TProjection : IProjection<TSnapshot>, new() where TSnapshot : class, new()
    {
        var options = new ProjectionOptions();
        configure?.Invoke(options);

        if (mode == ProjectionMode.Inline)
        {
            dbContextOptionsBuilders.Add((sp, dbContextOptionsBuilder) =>
            {
                dbContextOptionsBuilder.AddInterceptors(new ProjectionInterceptor<TProjection, TSnapshot>(options, sp));
            });
        }
        else
        {
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
}
