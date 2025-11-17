using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public interface IEventStoreBuilder<TDbContext>
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public IEventStoreBuilder<TDbContext> AddEventStoreInterfaceToServiceProvider();
    public IServiceCollection Services { get; }
    IEventStoreBuilder<TDbContext> AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
           where TProjection : IProjection<TSnapshot>, new()
           where TSnapshot : class, new();

    IEventStoreBuilder<TDbContext> AddSubscription<TSubscription>() 
        where TSubscription : ISubscription;

    IEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, string> factory);
    IEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, System.Data.Common.DbDataSource> factory);
    IEventStoreBuilder<TDbContext> AddSubscriptionDaemon(Func<IServiceProvider, System.Data.IDbConnection> factory);

}
