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
                        var processedCount = await ProcessNextBatchAsync(scope, subscription, stoppingToken);

                        if (processedCount == 0)
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
        return await ProcessNextBatchAsync(scope, subscriptionImpl, stoppingToken, 1, 1) > 0;
    }

    /// <summary>
    /// Processes the next available batch of events for a subscription.
    /// </summary>
    /// <param name="scope">The scoped service provider.</param>
    /// <param name="subscriptionImpl">The subscription instance.</param>
    /// <param name="stoppingToken">Cancellation token.</param>
    /// <returns>The number of events processed in the batch.</returns>
    internal Task<int> ProcessNextBatchAsync(IServiceScope scope, ISubscription subscriptionImpl, CancellationToken stoppingToken)
    {
        return ProcessNextBatchAsync(
            scope,
            subscriptionImpl,
            stoppingToken,
            Math.Max(1, _options.BatchSize),
            Math.Max(1, _options.CheckpointFrequency));
    }

    private async Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        ISubscription subscriptionImpl,
        CancellationToken stoppingToken,
        int batchSize,
        int checkpointFrequency)
    {
        var name = subscriptionImpl.GetType().AssemblyQualifiedName!;
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        try
        {
            var subscriptionSet = dbContext.Set<DbSubscription>();
            var subscription = await subscriptionSet.FindAsync([name], stoppingToken);
            var createdSubscription = false;

            if (subscription is null)
            {
                subscription = new DbSubscription
                {
                    SubscriptionAssemblyQualifiedName = name,
                };
                subscriptionSet.Add(subscription);
                createdSubscription = true;
                logger.LogInformation("Created new subscription entity for {Subscription}", name);
            }

            if (!CanProcess(subscription, name))
            {
                if (createdSubscription)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                    await transaction.CommitAsync(stoppingToken);
                }

                return 0;
            }

            var nextEvents = await dbContext.Events
                .Where(e => e.Sequence > subscription.Sequence)
                .OrderBy(e => e.Sequence)
                .Take(batchSize)
                .ToListAsync(stoppingToken);

            if (nextEvents.Count == 0)
            {
                if (createdSubscription)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                    await transaction.CommitAsync(stoppingToken);
                }

                return 0;
            }

            var registry = scope.ServiceProvider.GetService<EventTypeRegistry>();
            var processedCount = 0;
            long? lastProcessedSequence = null;

            foreach (var nextEvent in nextEvents)
            {
                try
                {
                    subscription.LastAttemptAt = DateTimeOffset.UtcNow;
                    var @event = nextEvent.ToEvent(registry);

                    if (subscriptionImpl is IScopedSubscription scoped)
                    {
                        await scoped.HandleAsync(dbContext, @event, stoppingToken);
                    }
                    else
                    {
                        await subscriptionImpl.Handle(@event, stoppingToken);
                    }

                    processedCount++;
                    lastProcessedSequence = nextEvent.Sequence;

                    subscription.State = SubscriptionState.Active;
                    subscription.LastError = null;
                    subscription.AttemptCount = 0;
                    subscription.NextAttemptAt = null;
                    subscription.FailedEventSequence = null;

                    if (processedCount % checkpointFrequency == 0)
                    {
                        subscription.Sequence = nextEvent.Sequence;
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Error processing event at sequence {Sequence} for subscription {Subscription}",
                        nextEvent.Sequence,
                        name);

                    if (lastProcessedSequence is long persistedSequence && subscription.Sequence != persistedSequence)
                    {
                        subscription.Sequence = persistedSequence;
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }

                    await PersistFailureAsync(name, nextEvent.Sequence, ex, stoppingToken);
                    return processedCount;
                }
            }

            if (lastProcessedSequence is long finalSequence && subscription.Sequence != finalSequence)
            {
                subscription.Sequence = finalSequence;
                await dbContext.SaveChangesAsync(stoppingToken);
            }

            await transaction.CommitAsync(stoppingToken);

            logger.LogInformation(
                "Processed {Count} events through sequence {Sequence} for subscription {Subscription}",
                processedCount,
                lastProcessedSequence,
                name);

            return processedCount;
        }
        catch
        {
            logger.LogWarning(
                "Subscription {Subscription} failed after processing one or more events",
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

    private bool CanProcess(DbSubscription subscription, string name)
    {
        var now = DateTimeOffset.UtcNow;

        switch (subscription.State)
        {
            case SubscriptionState.Active:
                return true;

            case SubscriptionState.Paused:
                logger.LogDebug("Subscription {Subscription} is paused, skipping", name);
                return false;

            case SubscriptionState.Faulted when subscription.NextAttemptAt.HasValue && subscription.NextAttemptAt.Value > now:
                logger.LogDebug(
                    "Subscription {Subscription} is faulted until {NextAttemptAt}, skipping",
                    name,
                    subscription.NextAttemptAt);
                return false;

            case SubscriptionState.Faulted:
                return true;

            case SubscriptionState.DeadLettered:
                logger.LogDebug("Subscription {Subscription} is dead-lettered, requires manual intervention", name);
                return false;

            default:
                return true;
        }
    }

    private async Task PersistFailureAsync(
        string subscriptionName,
        long failedSequence,
        Exception exception,
        CancellationToken cancellationToken)
    {
        using var errorScope = _serviceProvider.CreateScope();
        var errorContext = errorScope.ServiceProvider.GetRequiredService<TDbContext>();
        var subscriptionSet = errorContext.Set<DbSubscription>();
        var status = subscriptionSet.Local
            .FirstOrDefault(s => s.SubscriptionAssemblyQualifiedName == subscriptionName)
            ?? await subscriptionSet.FirstOrDefaultAsync(
                s => s.SubscriptionAssemblyQualifiedName == subscriptionName,
                cancellationToken);

        if (status is null)
        {
            status = new DbSubscription
            {
                SubscriptionAssemblyQualifiedName = subscriptionName
            };
            subscriptionSet.Add(status);
        }

        status.AttemptCount += 1;
        status.LastAttemptAt = DateTimeOffset.UtcNow;
        status.NextAttemptAt = status.LastAttemptAt.Value.Add(_options.RetryDelay);
        status.LastError = exception.ToString();
        status.FailedEventSequence = failedSequence;
        status.State = status.AttemptCount >= _options.MaxRetryAttempts
            ? SubscriptionState.DeadLettered
            : SubscriptionState.Faulted;

        await errorContext.SaveChangesAsync(cancellationToken);
    }
}
