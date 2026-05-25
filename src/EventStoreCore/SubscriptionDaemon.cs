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
                    var checkpointScopes = await GetCheckpointScopesAsync(name, stoppingToken);

                    if (checkpointScopes.Count == 0)
                    {
                        await Task.Delay(_options.PollingInterval, stoppingToken);
                        continue;
                    }

                    var processedAny = false;
                    foreach (var checkpointScope in checkpointScopes)
                    {
                        var acquired = await AcquireSubscriptionLockAsync(subscriptionType, checkpointScope, stoppingToken);

                        if (acquired == null)
                        {
                            continue;
                        }

                        await using (acquired)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var processedCount = await ProcessNextBatchAsync(scope, subscription, stoppingToken, checkpointScope);
                            processedAny = processedAny || processedCount > 0;
                        }

                        logger.LogInformation(
                            "Released lock for subscription {Subscription} in checkpoint scope {Scope}",
                            name,
                            checkpointScope);
                    }

                    if (!processedAny)
                    {
                        logger.LogInformation(
                            "No new events to process for subscription {Subscription}",
                            name);
                        await Task.Delay(_options.PollingInterval, stoppingToken);
                    }
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
        return await ProcessNextBatchAsync(scope, subscriptionImpl, stoppingToken, CheckpointScopeKey.Global, 1, 1) > 0;
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
            CheckpointScopeKey.Global,
            Math.Max(1, _options.BatchSize),
            Math.Max(1, _options.CheckpointFrequency));
    }

    internal Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        ISubscription subscriptionImpl,
        CancellationToken stoppingToken,
        Guid tenantId)
    {
        return ProcessNextBatchAsync(
            scope,
            subscriptionImpl,
            stoppingToken,
            CheckpointScopeKey.Tenant(tenantId),
            Math.Max(1, _options.BatchSize),
            Math.Max(1, _options.CheckpointFrequency));
    }

    private Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        ISubscription subscriptionImpl,
        CancellationToken stoppingToken,
        CheckpointScopeKey checkpointScope)
    {
        return ProcessNextBatchAsync(
            scope,
            subscriptionImpl,
            stoppingToken,
            checkpointScope,
            Math.Max(1, _options.BatchSize),
            Math.Max(1, _options.CheckpointFrequency));
    }

    private async Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        ISubscription subscriptionImpl,
        CancellationToken stoppingToken,
        CheckpointScopeKey checkpointScope,
        int batchSize,
        int checkpointFrequency)
    {
        var name = subscriptionImpl.GetType().AssemblyQualifiedName!;
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        try
        {
            var subscriptionSet = dbContext.Set<DbSubscription>();
            var subscription = await subscriptionSet.FirstOrDefaultAsync(
                s => s.SubscriptionAssemblyQualifiedName == name &&
                    s.CheckpointScope == checkpointScope.Scope &&
                    s.TenantId == checkpointScope.TenantId,
                stoppingToken);
            var createdSubscription = false;

            if (subscription is null)
            {
                subscription = new DbSubscription
                {
                    SubscriptionAssemblyQualifiedName = name,
                    CheckpointScope = checkpointScope.Scope,
                    TenantId = checkpointScope.TenantId
                };
                subscriptionSet.Add(subscription);
                createdSubscription = true;
                logger.LogInformation(
                    "Created new subscription entity for {Subscription} in checkpoint scope {Scope}",
                    name,
                    checkpointScope);
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

            var nextEvents = await ApplyCheckpointScope(dbContext.Events, checkpointScope)
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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
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
                    }

                    PersistFailure(subscription, nextEvent.Sequence, ex);
                    await dbContext.SaveChangesAsync(stoppingToken);
                    await transaction.CommitAsync(stoppingToken);
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
        return await AcquireSubscriptionLockAsync(typeof(TSub), CheckpointScopeKey.Global, cancellationToken);
    }

    /// <summary>
    /// Acquires a distributed lock for the specified subscription type.
    /// </summary>
    /// <param name="subType">The subscription type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lock handle or null when lock acquisition fails.</returns>
    private async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(Type subType, CancellationToken cancellationToken)
    {
        return await AcquireSubscriptionLockAsync(subType, CheckpointScopeKey.Global, cancellationToken);
    }

    private async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(
        Type subType,
        CheckpointScopeKey checkpointScope,
        CancellationToken cancellationToken)
    {
        var subscriptionName = subType.AssemblyQualifiedName!;
        var lockName = $"{subscriptionName}{checkpointScope.LockSuffix}";


        try
        {
            logger.LogInformation(
                "Attempting to acquire lock for subscription {Subscription} in checkpoint scope {Scope}",
                subscriptionName,
                checkpointScope);
            var acquired = await _distributedLockProvider
                  .AcquireLockAsync(lockName, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);

            if (acquired == null)
            {
                logger.LogInformation(
                    "Could not acquire lock for subscription {Subscription} in checkpoint scope {Scope}, another instance may be running.",
                    subscriptionName,
                    checkpointScope);
                return null;
            }

            logger.LogInformation(
                "Acquired lock for subscription {Subscription} in checkpoint scope {Scope}",
                subscriptionName,
                checkpointScope);
            return acquired as IAsyncDisposable ?? acquired;
        }
        catch (TimeoutException)
        {
            logger.LogInformation(
                    "Could not acquire lock for subscription {Subscription} in checkpoint scope {Scope}, another instance may be running.",
                    subscriptionName,
                    checkpointScope);
            return null;
        }
    }

    private async Task<IReadOnlyList<CheckpointScopeKey>> GetCheckpointScopesAsync(
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        if (_options.CheckpointScope == CheckpointScope.Global)
        {
            return [CheckpointScopeKey.Global];
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var eventTenantIds = await dbContext.Events
            .AsNoTracking()
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var checkpointTenantIds = await dbContext.Set<DbSubscription>()
            .AsNoTracking()
            .Where(s =>
                s.SubscriptionAssemblyQualifiedName == subscriptionName &&
                s.CheckpointScope == CheckpointScope.Tenant)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return eventTenantIds
            .Concat(checkpointTenantIds)
            .Distinct()
            .OrderBy(tenantId => tenantId)
            .Select(CheckpointScopeKey.Tenant)
            .ToArray();
    }

    private static IQueryable<DbEvent> ApplyCheckpointScope(
        IQueryable<DbEvent> query,
        CheckpointScopeKey checkpointScope)
    {
        return checkpointScope.IsTenant
            ? query.Where(e => e.TenantId == checkpointScope.TenantId)
            : query;
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

    private void PersistFailure(
        DbSubscription subscription,
        long failedSequence,
        Exception exception)
    {
        subscription.AttemptCount += 1;
        subscription.LastAttemptAt ??= DateTimeOffset.UtcNow;
        subscription.NextAttemptAt = subscription.LastAttemptAt.Value.Add(_options.RetryDelay);
        subscription.LastError = exception.ToString();
        subscription.FailedEventSequence = failedSequence;
        subscription.State = subscription.AttemptCount >= _options.MaxRetryAttempts
            ? SubscriptionState.DeadLettered
            : SubscriptionState.Faulted;
    }
}
