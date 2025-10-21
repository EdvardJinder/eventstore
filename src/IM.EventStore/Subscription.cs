using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IM.EventStore;

internal sealed class Subscription<TSubscription, TDbContext>(
    ILogger<Subscription<TSubscription, TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider
    )
    : BackgroundService
    where TSubscription : ISubscription
    where TDbContext : DbContext
{
    private const int LockAcquireRetryIntervalSeconds = 10;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IDistributedLockProvider _distributedLockProvider = distributedLockProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var acquired = await AcquireSubscriptionLockAsync(stoppingToken);

                    if (acquired == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(LockAcquireRetryIntervalSeconds), stoppingToken);
                        continue;
                    }

                    // Use async disposal of the acquired lock (assumes the returned lock is IAsyncDisposable)
                    await using (acquired)
                    {
                        using var scope = _serviceProvider.CreateScope();

                        var processed = await ProcessNextEventAsync(scope, stoppingToken);

                        if (!processed)
                        {
                            logger.LogInformation(
                                "No new events to process for subscription {Subscription}",
                                typeof(TSubscription).Name);

                            await Task.Delay(TimeSpan.FromSeconds(LockAcquireRetryIntervalSeconds), stoppingToken);
                            continue;
                        }
                    }

                    logger.LogInformation("Released lock for subscription {Subscription}", typeof(TSubscription).Name);
                }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in subscription {Subscription}", typeof(TSubscription).Name);
        }
    }

    // Extracted, testable unit: processes a single next event (if any) using the provided scope.
    // Returns true when an event was processed and subscription updated; false when no event was found.
    internal async Task<bool> ProcessNextEventAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Find subscription entity by assembly-qualified name key
        var key = typeof(TSubscription).AssemblyQualifiedName!;
        var subscription = await dbContext.Set<DbSubscription>()
            .FindAsync(new object[] { key }, stoppingToken);

        if (subscription is null)
        {
            subscription = new DbSubscription
            {
                SubscriptionAssemblyQualifiedName = key,
            };
            dbContext.Set<DbSubscription>().Add(subscription);
            await dbContext.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Created new subscription entity for {Subscription}", typeof(TSubscription).Name);
        }

        var nextEvent = await dbContext.Events()
            .Where(e => e.Sequence > subscription.Sequence)
            .FirstOrDefaultAsync(stoppingToken);

        if (nextEvent is null)
        {
            return false;
        }

        var @event = nextEvent.ToEvent();

        await TSubscription.Handle(@event, scope.ServiceProvider, stoppingToken);

        subscription.Sequence = nextEvent.Sequence;
        await dbContext.SaveChangesAsync(stoppingToken);

        return true;
    }

    // Extracted, testable unit: tries to acquire the distributed lock for this subscription.
    // Returns the acquired lock as IAsyncDisposable when successful; null when not acquired.
    internal async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(CancellationToken cancellationToken)
    {
        var acquired = await _distributedLockProvider
            .AcquireLockAsync(typeof(TSubscription).AssemblyQualifiedName!, cancellationToken: cancellationToken);

        if (acquired == null)
        {
            logger.LogInformation(
                "Could not acquire lock for subscription {Subscription}, another instance may be running.",
                typeof(TSubscription).Name);
            return null;
        }

        logger.LogInformation("Acquired lock for subscription {Subscription}", typeof(TSubscription).Name);
        return acquired as IAsyncDisposable ?? acquired; // cast to IAsyncDisposable when possible
    }
}