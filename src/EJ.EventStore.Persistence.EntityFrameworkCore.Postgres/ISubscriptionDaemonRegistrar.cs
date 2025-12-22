using Medallion.Threading;

namespace EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;

internal interface ISubscriptionDaemonRegistrar
{
    void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory);
}
