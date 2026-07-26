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
/// <param name="timeProvider">Optional clock used for delays and operational timestamps.</param>
public sealed class SubscriptionDaemon<TDbContext>(
    ILogger<SubscriptionDaemon<TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider,
    IOptions<SubscriptionOptions> options,
    TimeProvider? timeProvider = null
    )
    : BackgroundService
    where TDbContext : DbContext
{
    private readonly SubscriptionOptions _options = options.Value;

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IDistributedLockProvider _distributedLockProvider = distributedLockProvider;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registrations = GetRegistrations();

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var registration in registrations)
            {
                var subscription = registration.Subscription;
                var subscriptionType = subscription.GetType();
                var name = registration.Name;

                try
                {
                    var checkpointScopes = await GetCheckpointScopesAsync(name, stoppingToken);

                    if (checkpointScopes.Count == 0)
                    {
                        await Task.Delay(_options.PollingInterval, _timeProvider, stoppingToken);
                        continue;
                    }

                    var processedAny = false;
                    foreach (var checkpointScope in checkpointScopes)
                    {
                        var acquired = await AcquireSubscriptionLockAsync(name, checkpointScope, stoppingToken);

                        if (acquired == null)
                        {
                            continue;
                        }

                        await using (acquired)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var processedCount = await ProcessNextBatchAsync(
                                scope,
                                registration,
                                stoppingToken,
                                checkpointScope);
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
                        await Task.Delay(_options.PollingInterval, _timeProvider, stoppingToken);
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
                        EventStoreDaemonDiagnostics.Retry(name, "subscription");
                        await Task.Delay(_options.RetryDelay, _timeProvider, stoppingToken);
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
        return await ProcessNextBatchAsync(
            scope,
            CreateDefaultRegistration(subscriptionImpl),
            stoppingToken,
            CheckpointScopeKey.Global,
            1,
            1) > 0;
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
            CreateDefaultRegistration(subscriptionImpl),
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
            CreateDefaultRegistration(subscriptionImpl),
            stoppingToken,
            CheckpointScopeKey.Tenant(tenantId),
            Math.Max(1, _options.BatchSize),
            Math.Max(1, _options.CheckpointFrequency));
    }

    internal Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        SubscriptionRegistration registration,
        CancellationToken stoppingToken,
        CheckpointScopeKey checkpointScope)
    {
        return ProcessNextBatchAsync(
            scope,
            registration,
            stoppingToken,
            checkpointScope,
            Math.Max(1, _options.BatchSize),
            Math.Max(1, _options.CheckpointFrequency));
    }

    private async Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        SubscriptionRegistration registration,
        CancellationToken stoppingToken,
        CheckpointScopeKey checkpointScope,
        int batchSize,
        int checkpointFrequency)
    {
        var subscriptionImpl = registration.Subscription;
        var name = registration.Name;
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var startedAt = _timeProvider.GetTimestamp();
        using var activity = EventStoreDaemonDiagnostics.StartBatch(name, "subscription");

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

                _serviceProvider.GetService<DaemonHealthMonitor>()?.Heartbeat(name, "subscription");
                return 0;
            }

            var registry = scope.ServiceProvider.GetService<EventTypeRegistry>();
            var serializer = scope.ServiceProvider.GetService<IEventStoreSerializer>()
                ?? new SystemTextJsonEventStoreSerializer();
            var processedCount = 0;
            long? lastProcessedSequence = null;

            foreach (var nextEvent in nextEvents)
            {
                var savepointName = $"before_event_{nextEvent.Sequence}";
                if (transaction.SupportsSavepoints)
                {
                    await transaction.CreateSavepointAsync(savepointName, stoppingToken);
                }

                try
                {
                    subscription.LastAttemptAt = _timeProvider.GetUtcNow();

                    if (!registration.Options.MatchesPersisted(nextEvent))
                    {
                        processedCount++;
                        lastProcessedSequence = nextEvent.Sequence;
                        subscription.Sequence = nextEvent.Sequence;
                        MarkProcessed(subscription);
                        continue;
                    }

                    IEvent @event;
                    try
                    {
                        @event = nextEvent.ToEvent(registry, serializer);
                    }
                    catch (EventMaterializationException ex)
                    {
                        if (await HandleUnknownEventAsync(registration, subscription, nextEvent, ex, stoppingToken))
                        {
                            processedCount++;
                            lastProcessedSequence = nextEvent.Sequence;
                            subscription.Sequence = nextEvent.Sequence;
                            MarkProcessed(subscription);
                            continue;
                        }

                        throw;
                    }

                    if (!registration.Options.MatchesMaterialized(@event.EventType))
                    {
                        processedCount++;
                        lastProcessedSequence = nextEvent.Sequence;
                        subscription.Sequence = nextEvent.Sequence;
                        MarkProcessed(subscription);
                        continue;
                    }

                    var isScopedSubscription = subscriptionImpl is IScopedSubscription;
                    if (subscriptionImpl is IScopedSubscription scoped)
                    {
                        await scoped.HandleAsync(
                            dbContext,
                            scope.ServiceProvider,
                            @event,
                            stoppingToken);
                    }
                    else
                    {
                        await subscriptionImpl.Handle(@event, stoppingToken);
                    }

                    processedCount++;
                    lastProcessedSequence = nextEvent.Sequence;

                    MarkProcessed(subscription);

                    var shouldCheckpoint = processedCount % checkpointFrequency == 0;
                    if (shouldCheckpoint)
                    {
                        subscription.Sequence = nextEvent.Sequence;
                    }

                    // Persist handler mutations inside the batch transaction before establishing
                    // the next event savepoint. They remain invisible until the batch commits.
                    if (isScopedSubscription || shouldCheckpoint)
                    {
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    if (transaction.SupportsSavepoints)
                    {
                        await transaction.ReleaseSavepointAsync(savepointName, stoppingToken);
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

                    if (transaction.SupportsSavepoints)
                    {
                        await transaction.RollbackToSavepointAsync(savepointName, stoppingToken);
                    }
                    dbContext.ChangeTracker.Clear();

                    subscription = await subscriptionSet.FirstOrDefaultAsync(
                        s => s.SubscriptionAssemblyQualifiedName == name &&
                            s.CheckpointScope == checkpointScope.Scope &&
                            s.TenantId == checkpointScope.TenantId,
                        stoppingToken)
                        ?? new DbSubscription
                        {
                            SubscriptionAssemblyQualifiedName = name,
                            CheckpointScope = checkpointScope.Scope,
                            TenantId = checkpointScope.TenantId
                        };

                    if (dbContext.Entry(subscription).State == EntityState.Detached)
                    {
                        subscriptionSet.Add(subscription);
                    }

                    if (lastProcessedSequence is long persistedSequence)
                    {
                        subscription.Sequence = persistedSequence;
                    }

                    PersistFailure(subscription, nextEvent.Sequence, ex);
                    if (registration.Options.UnknownEventPolicy == UnknownEventPolicy.Quarantine &&
                        ex is EventMaterializationException)
                    {
                        subscription.State = SubscriptionState.DeadLettered;
                        subscription.NextAttemptAt = null;
                    }
                    await PublishFaultAsync(
                        name,
                        "subscription",
                        subscription.State.ToString(),
                        checkpointScope,
                        nextEvent.Sequence,
                        ex,
                        stoppingToken);
                    EventStoreDaemonDiagnostics.Failed(name, "subscription");
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
            var checkpointLag = await ApplyCheckpointScope(dbContext.Events, checkpointScope)
                .LongCountAsync(e => e.Sequence > subscription.Sequence, stoppingToken);
            EventStoreDaemonDiagnostics.BatchCompleted(
                name,
                "subscription",
                processedCount,
                _timeProvider.GetElapsedTime(startedAt),
                checkpointLag);
            _serviceProvider.GetService<DaemonHealthMonitor>()?.Heartbeat(name, "subscription");

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
        return await AcquireSubscriptionLockAsync(
            typeof(TSub).AssemblyQualifiedName!,
            CheckpointScopeKey.Global,
            cancellationToken);
    }

    /// <summary>
    /// Acquires a distributed lock for the specified subscription type.
    /// </summary>
    /// <param name="subType">The subscription type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lock handle or null when lock acquisition fails.</returns>
    private async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(Type subType, CancellationToken cancellationToken)
    {
        return await AcquireSubscriptionLockAsync(
            subType.AssemblyQualifiedName!,
            CheckpointScopeKey.Global,
            cancellationToken);
    }

    private async Task<IAsyncDisposable?> AcquireSubscriptionLockAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken cancellationToken)
    {
        var lockName = $"{subscriptionName}{checkpointScope.LockSuffix}";


        try
        {
            logger.LogInformation(
                "Attempting to acquire lock for subscription {Subscription} in checkpoint scope {Scope}",
                subscriptionName,
                checkpointScope);
            var acquired = await _distributedLockProvider
                  .AcquireLockAsync(lockName, ValidateLockTimeout(_options.LockTimeout), cancellationToken: cancellationToken);

            if (acquired == null)
            {
                EventStoreDaemonDiagnostics.LockContended(subscriptionName, "subscription");
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
            EventStoreDaemonDiagnostics.LockContended(subscriptionName, "subscription");
            logger.LogInformation(
                    "Could not acquire lock for subscription {Subscription} in checkpoint scope {Scope}, another instance may be running.",
                    subscriptionName,
                    checkpointScope);
            return null;
        }
    }

    private static TimeSpan ValidateLockTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new InvalidOperationException(
                $"{nameof(SubscriptionOptions.LockTimeout)} must be non-negative or Timeout.InfiniteTimeSpan.");
        }

        return timeout;
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
        var now = _timeProvider.GetUtcNow();

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
        subscription.LastAttemptAt ??= _timeProvider.GetUtcNow();
        subscription.NextAttemptAt = subscription.LastAttemptAt.Value.Add(_options.RetryDelay);
        subscription.LastError = exception.ToString();
        subscription.FailedEventSequence = failedSequence;
        subscription.State = subscription.AttemptCount >= _options.MaxRetryAttempts
            ? SubscriptionState.DeadLettered
            : SubscriptionState.Faulted;
    }

    private static void MarkProcessed(DbSubscription subscription)
    {
        subscription.State = SubscriptionState.Active;
        subscription.LastError = null;
        subscription.AttemptCount = 0;
        subscription.NextAttemptAt = null;
        subscription.FailedEventSequence = null;
    }

    private IReadOnlyList<SubscriptionRegistration> GetRegistrations()
    {
        var registrations = _serviceProvider.GetServices<SubscriptionRegistration>().ToList();
        var registeredTypes = registrations
            .Select(registration => registration.Subscription.GetType())
            .ToHashSet();

        registrations.AddRange(
            _serviceProvider.GetServices<ISubscription>()
                .Where(subscription => !registeredTypes.Contains(subscription.GetType()))
                .Select(CreateDefaultRegistration));

        var duplicate = registrations
            .GroupBy(registration => registration.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Multiple subscriptions use the logical name '{duplicate.Key}'. Logical names must be unique.");
        }

        return registrations;
    }

    private static SubscriptionRegistration CreateDefaultRegistration(ISubscription subscription) =>
        new()
        {
            Name = subscription.GetType().AssemblyQualifiedName!,
            Subscription = subscription,
            Options = new SubscriptionRegistrationOptions()
        };

    private async ValueTask<bool> HandleUnknownEventAsync(
        SubscriptionRegistration registration,
        DbSubscription subscription,
        DbEvent dbEvent,
        EventMaterializationException exception,
        CancellationToken ct)
    {
        switch (registration.Options.UnknownEventPolicy)
        {
            case UnknownEventPolicy.Skip:
                logger.LogWarning(
                    exception,
                    "Skipping unknown event at sequence {Sequence} for subscription {Subscription}",
                    dbEvent.Sequence,
                    registration.Name);
                return true;

            case UnknownEventPolicy.Custom:
                var handler = registration.Options.UnknownEventHandler
                    ?? throw new InvalidOperationException(
                        "UnknownEventPolicy.Custom requires a handler configured with HandleUnknown.");
                await handler(
                    new UnknownEventContext(
                        dbEvent.EventId,
                        dbEvent.StreamId,
                        dbEvent.StreamType,
                        dbEvent.TenantId,
                        dbEvent.Sequence,
                        dbEvent.Version,
                        dbEvent.TypeName,
                        dbEvent.Type,
                        dbEvent.Data,
                        dbEvent.Timestamp,
                        exception),
                    ct);
                return true;

            case UnknownEventPolicy.Fail:
            case UnknownEventPolicy.Quarantine:
            default:
                return false;
        }
    }

    private async ValueTask PublishFaultAsync(
        string identity,
        string kind,
        string state,
        CheckpointScopeKey checkpointScope,
        long? failedSequence,
        Exception exception,
        CancellationToken ct)
    {
        var notification = new DaemonFaultNotification(
            identity,
            kind,
            state,
            checkpointScope.Scope,
            checkpointScope.IsTenant ? checkpointScope.TenantId : null,
            failedSequence,
            exception,
            _timeProvider.GetUtcNow());

        foreach (var observer in _serviceProvider.GetServices<IDaemonFaultObserver>())
        {
            try
            {
                await observer.OnFaultAsync(notification, ct);
            }
            catch (Exception observerException)
            {
                logger.LogWarning(
                    observerException,
                    "Daemon fault observer {ObserverType} failed for subscription {Subscription}",
                    observer.GetType(),
                    identity);
            }
        }
        _serviceProvider.GetService<DaemonHealthMonitor>()?.Fault(identity, kind, exception);
    }
}
