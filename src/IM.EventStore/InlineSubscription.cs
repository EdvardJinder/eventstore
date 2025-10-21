using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IM.EventStore;

internal sealed class InlineSubscription<TDbContext>(
    string name,
    ILogger<Subscription<TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider,
    Func<IEvent, IServiceProvider, CancellationToken, Task> handleAsync
    )
    : Subscription<TDbContext>(name, logger, serviceProvider, distributedLockProvider)
    where TDbContext : DbContext
{
    protected override Task HandleEventAsync(IEvent @event, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        return handleAsync(@event, serviceProvider, cancellationToken);
    }
}
