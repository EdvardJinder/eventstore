using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStoreCore;

/// <summary>
/// Background service that processes subscriptions asynchronously.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
/// <param name="logger">The logger instance.</param>
/// <param name="serviceProvider">Service provider for resolving scoped services.</param>
/// <param name="distributedLockProvider">Distributed lock provider.</param>
/// <param name="options">Subscription options.</param>
public sealed class SubscriptionDaemon<TDbContext>(
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

    /// <inheritdoc />
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


    /// <summary>
    /// Processes the next available event for a subscription.
    /// </summary>
    /// <param name="scope">The scoped service provider.</param>
    /// <param name="subscriptionImpl">The subscription instance.</param>
    /// <param name="stoppingToken">Cancellation token.</param>
    /// <returns>True when an event was processed.</returns>
    internal async Task<bool> ProcessNextEventAsync(IServiceScope scope, ISubscription subscriptionImpl, CancellationToken stoppingToken)
    {
        var name = subscriptionImpl.GetType().AssemblyQualifiedName!;

        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();


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
                .OrderBy(e => e.Sequence) // Ensure ordering
                .FirstOrDefaultAsync(stoppingToken);

            if (nextEvent is null)
            {
                return false;
            }

            var registry = scope.ServiceProvider.GetService<EventTypeRegistry>();
            var @event = nextEvent.ToEvent(registry);

            if (subscriptionImpl is IScopedSubscription scoped)
            {
                await scoped.HandleAsync(dbContext, @event, stoppingToken);
            }
            else
            {
                await subscriptionImpl.Handle(@event, stoppingToken);
            }

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
            throw;
        }
    }

    /// <summary>
    /// Acquires a distributed lock for a subscription type.
    /// </summary>
    /// <typeparam name="TSub">The subscription type.</typeparam>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lock handle or null when lock acquisition fails.</returns>
    internal async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync<TSub>(CancellationToken cancellationToken)
        where TSub : ISubscription
    {
        return await AcquireSubscriptionLockAsync(typeof(TSub), cancellationToken);
    }

    /// <summary>
    /// Acquires a distributed lock for the specified subscription type.
    /// </summary>
    /// <param name="subType">The subscription type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lock handle or null when lock acquisition fails.</returns>
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
            return acquired as IAsyncDisposable ?? acquired;
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
