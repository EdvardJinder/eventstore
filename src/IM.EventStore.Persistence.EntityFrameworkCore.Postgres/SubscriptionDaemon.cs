using IM.EventStore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IM.EventStore.Persistence.EntityFrameworkCore.Postgres;

internal sealed class SubscriptionDaemon<TDbContext>(
    ILogger<SubscriptionDaemon<TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider,
    IOptions<SubscriptionOptions> options
    )
    : BackgroundService
    where TDbContext : DbContext
{
    private readonly SubscriptionOptions _options = options.Value;

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IDistributedLockProvider _distributedLockProvider = distributedLockProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = _serviceProvider.GetServices<ISubscription>();

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var subscription in subscriptions)
            {
                var subscriptionType = subscription.GetType();
                var name = subscriptionType.AssemblyQualifiedName!;

                try
                {
                    var acquired = await AcquireSubscriptionLockAsync(subscriptionType, stoppingToken);

                    if (acquired == null)
                    {
                        await Task.Delay(_options.LockTimeout, stoppingToken);
                        continue;
                    }

                    await using (acquired)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var processed = await ProcessNextEventAsync(scope, subscription, stoppingToken);

                        if (!processed)
                        {
                            logger.LogInformation(
                                "No new events to process for subscription {Subscription}",
                                name);
                            await Task.Delay(_options.PollingInterval, stoppingToken);
                        }
                    }

                    logger.LogInformation("Released lock for subscription {Subscription}", name);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("Subscription {Subscription} stopping gracefully", name);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error processing events for subscription {Subscription}. Retrying in {RetrySeconds} seconds",
                        name, _options.RetryDelay);

                    try
                    {
                        await Task.Delay(_options.RetryDelay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }

    internal async Task<bool> ProcessNextEventAsync(IServiceScope scope, ISubscription subscriptionImpl, CancellationToken stoppingToken)
    {
        var name = subscriptionImpl.GetType().AssemblyQualifiedName!;

        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Use explicit transaction for atomic processing
        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        try
        {
            var subscription = await dbContext.Set<DbSubscription>().FindAsync([name], stoppingToken);
            if (subscription is null)
            {
                subscription = new DbSubscription
                {
                    SubscriptionAssemblyQualifiedName = name,
                };
                dbContext.Set<DbSubscription>().Add(subscription);
                await dbContext.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Created new subscription entity for {Subscription}", name);
            }

            var nextEvent = await dbContext.Events
                .Where(e => e.Sequence > subscription.Sequence)
                .OrderBy(e => e.Sequence) // ✅ Ensure ordering
                .FirstOrDefaultAsync(stoppingToken);

            if (nextEvent is null)
            {
                return false;
            }

            var @event = nextEvent.ToEvent();

            // Handler executes within transaction
            await subscriptionImpl.Handle(@event, stoppingToken);

            subscription.Sequence = nextEvent.Sequence;
            await dbContext.SaveChangesAsync(stoppingToken);

            await transaction.CommitAsync(stoppingToken);

            logger.LogInformation(
                "Processed event {EventId} (Sequence {Sequence}) for subscription {Subscription}",
                @event.Id, nextEvent.Sequence, name);

            return true;
        }
        catch
        {
            logger.LogWarning(
                "Rolling back transaction for subscription {Subscription}",
                name);
            throw; // Re-throw to trigger outer retry logic
        }
    }


    // Extracted, testable unit: tries to acquire the distributed lock for this subscription.
    // Returns the acquired lock as IAsyncDisposable when successful; null when not acquired.
    internal async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync<TSub>(CancellationToken cancellationToken)
        where TSub : ISubscription
    {
        return await AcquireSubscriptionLockAsync(typeof(TSub), cancellationToken);
    }
    private async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(Type subType, CancellationToken cancellationToken)
    {
        var name = subType.AssemblyQualifiedName!;

        try
        {
            logger.LogInformation("Attempting to acquire lock for subscription {Subscription}", name);
            var acquired = await _distributedLockProvider
                  .AcquireLockAsync(name, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);

            if (acquired == null)
            {
                logger.LogInformation(
                    "Could not acquire lock for subscription {Subscription}, another instance may be running.",
                    name);
                return null;
            }

            logger.LogInformation("Acquired lock for subscription {Subscription}", name);
            return acquired as IAsyncDisposable ?? acquired; // cast to IAsyncDisposable when possible
        }
        catch (TimeoutException)
        {
            logger.LogInformation(
                    "Could not acquire lock for subscription {Subscription}, another instance may be running.",
                    name);
            return null;
        }
    }
}