using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IM.EventStore;

internal sealed class StaticSubscription<TSubscription, TDbContext>(
    string name,
    ILogger<Subscription<TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider
    )
    : Subscription<TDbContext>(name, logger, serviceProvider, distributedLockProvider)
    where TSubscription : ISubscription
    where TDbContext : DbContext
{
    protected override Task HandleEventAsync(IEvent @event, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        return TSubscription.Handle(@event, serviceProvider, cancellationToken);
    }
}
