using Medallion.Threading;

namespace IM.EventStore.Persistence.EntityFrameworkCore.Postgres;

internal interface ISubscriptionDaemonRegistrar
{
    void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory);
}
