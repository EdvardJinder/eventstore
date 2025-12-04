using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;

public interface IEfCoreEventStoreBuilder<TDbContext> 
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public IServiceCollection Services { get; }

    IEfCoreEventStoreBuilder<TDbContext> AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
       where TProjection : IProjection<TSnapshot>, new()
       where TSnapshot : class, new();

    IEfCoreEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, string> factory);
    IEfCoreEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, System.Data.Common.DbDataSource> factory);
    IEfCoreEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, System.Data.IDbConnection> factory);


}