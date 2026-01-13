using Medallion.Threading;

namespace EventStoreCore;


/// <summary>
/// Interface for registering the projection daemon.
/// </summary>
internal interface IProjectionDaemonRegistrar
{
    /// <summary>
    /// Registers the projection daemon with a custom distributed lock provider.
    /// </summary>
    void AddProjectionDaemon(
        Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory,
        Action<ProjectionDaemonOptions>? configure = null);
}
