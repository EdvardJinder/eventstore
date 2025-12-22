using Medallion.Threading;

namespace EventStoreCore.Persistence.EntityFrameworkCore.Postgres;

internal interface ISubscriptionDaemonRegistrar
{
    void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory);
}
