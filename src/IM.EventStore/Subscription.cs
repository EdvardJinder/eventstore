using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IM.EventStore;

internal sealed class Subscription<TSubscription, TDbContext>(
    ILogger<Subscription<TSubscription, TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider,
    IOptions<SubscriptionOptions> options // ✅ Inject options
    )
    : BackgroundService
    where TSubscription : ISubscription
    where TDbContext : DbContext
{
    private readonly SubscriptionOptions _options = options.Value;
    
    // Use _options.PollingInterval instead of hardcoded value

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IDistributedLockProvider _distributedLockProvider = distributedLockProvider;

    public static string Name => typeof(TSubscription).AssemblyQualifiedName!;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var acquired = await AcquireSubscriptionLockAsync(stoppingToken);

                if (acquired == null)
                {
                    await Task.Delay(_options.LockTimeout, stoppingToken);
                    continue;
                }

                await using (acquired)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var processed = await ProcessNextEventAsync(scope, stoppingToken);

                    if (!processed)
                    {
                        logger.LogInformation(
                            "No new events to process for subscription {Subscription}",
                            Name);
                        await Task.Delay(_options.PollingInterval, stoppingToken);
                    }
                }

                logger.LogInformation("Released lock for subscription {Subscription}", Name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Subscription {Subscription} stopping gracefully", Name);
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error processing events for subscription {Subscription}. Retrying in {RetrySeconds} seconds",
                    Name, _options.RetryDelay);

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

    // Extracted, testable unit: processes a single next event (if any) using the provided scope.
    // Returns true when an event was processed and subscription updated; false when no event was found.
    internal async Task<bool> ProcessNextEventAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Use explicit transaction for atomic processing
        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);
        
        try
        {
            var subscription = await dbContext.Set<DbSubscription>().FindAsync([Name], stoppingToken);
            if (subscription is null)
            {
                subscription = new DbSubscription
                {
                    SubscriptionAssemblyQualifiedName = Name,
                };
                dbContext.Set<DbSubscription>().Add(subscription);
                await dbContext.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Created new subscription entity for {Subscription}", Name);
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
            await TSubscription.Handle(@event, scope.ServiceProvider, stoppingToken);

            subscription.Sequence = nextEvent.Sequence;
            await dbContext.SaveChangesAsync(stoppingToken);
            
            await transaction.CommitAsync(stoppingToken);
            
            logger.LogInformation(
                "Processed event {EventId} (Sequence {Sequence}) for subscription {Subscription}",
                @event.Id, nextEvent.Sequence, Name);

            return true;
        }
        catch
        {
            logger.LogWarning(
                "Rolling back transaction for subscription {Subscription}",
                Name);
            throw; // Re-throw to trigger outer retry logic
        }
    }

    // Extracted, testable unit: tries to acquire the distributed lock for this subscription.
    // Returns the acquired lock as IAsyncDisposable when successful; null when not acquired.
    internal async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(CancellationToken cancellationToken)
    {
        var acquired = await _distributedLockProvider
            .AcquireLockAsync(Name, cancellationToken: cancellationToken);

        if (acquired == null)
        {
            logger.LogInformation(
                "Could not acquire lock for subscription {Subscription}, another instance may be running.",
                Name);
            return null;
        }

        logger.LogInformation("Acquired lock for subscription {Subscription}", Name);
        return acquired as IAsyncDisposable ?? acquired; // cast to IAsyncDisposable when possible
    }
}