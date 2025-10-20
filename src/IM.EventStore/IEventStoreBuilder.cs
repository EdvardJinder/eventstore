using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public interface IEventStoreBuilder
{
    internal IServiceCollection Services { get; }
    IEventStoreBuilder AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
           where TProjection : IProjection<TSnapshot>, new()
           where TSnapshot : class, new();

    IEventStoreBuilder AddSubscription<TSubscription>() 
        where TSubscription : ISubscription;

    IEventStoreBuilder AddSubscriptionDaemon(string connectionString);
    IEventStoreBuilder AddSubscriptionDaemon(System.Data.Common.DbDataSource dbDataSource);
    IEventStoreBuilder AddSubscriptionDaemon(System.Data.IDbConnection dbConnection);

}
