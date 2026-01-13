using Medallion.Threading;

namespace EventStoreCore;


internal interface ISubscriptionDaemonRegistrar
{
    void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory);
}
