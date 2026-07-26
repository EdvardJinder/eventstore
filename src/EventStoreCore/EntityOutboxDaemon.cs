using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStoreCore;

/// <summary>
/// Dispatches captured EF entity events to independently checkpointed outbox subscriptions.
/// </summary>
/// <typeparam name="TDbContext">The DbContext containing the outbox tables.</typeparam>
/// <param name="logger">The logger.</param>
/// <param name="serviceProvider">The application service provider.</param>
/// <param name="distributedLockProvider">The distributed lock provider.</param>
/// <param name="options">The daemon options.</param>
/// <param name="timeProvider">The time provider.</param>
public sealed class EntityOutboxDaemon<TDbContext>(
    ILogger<EntityOutboxDaemon<TDbContext>> logger,
    IServiceProvider serviceProvider,
    IDistributedLockProvider distributedLockProvider,
    IOptions<EntityOutboxOptions> options,
    TimeProvider timeProvider) : BackgroundService
    where TDbContext : DbContext
{
    private readonly EntityOutboxOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registrations = serviceProvider
            .GetServices<OutboxSubscriptionRegistration>()
            .ToArray();

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;

            foreach (var registration in registrations)
            {
                try
                {
                    foreach (var checkpointScope in await GetCheckpointScopesAsync(registration, stoppingToken))
                    {
                        await using var acquired = await AcquireLockAsync(
                            registration.Name,
                            checkpointScope,
                            stoppingToken);
                        if (acquired is null)
                        {
                            continue;
                        }

                        using var handlerScope = serviceProvider.CreateScope();
                        using var checkpointScopeServices = serviceProvider.CreateScope();
                        var subscription = registration.Resolve(handlerScope.ServiceProvider);
                        processedAny |= await ProcessNextBatchAsync(
                            checkpointScopeServices,
                            subscription,
                            registration,
                            checkpointScope,
                            stoppingToken) > 0;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Entity outbox subscription {Subscription} failed.",
                        registration.Name);
                }
            }

            if (!processedAny)
            {
                await Task.Delay(_options.PollingInterval, timeProvider, stoppingToken);
            }
        }
    }

    internal async Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        IOutboxSubscription subscription,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var registration = serviceProvider
            .GetServices<OutboxSubscriptionRegistration>()
            .SingleOrDefault(candidate =>
                candidate.SubscriptionType == subscription.GetType())
            ?? new OutboxSubscriptionRegistration(
                subscription.GetType().AssemblyQualifiedName
                    ?? throw new InvalidOperationException(
                        "An outbox subscription type has no assembly-qualified name."),
                subscription.GetType(),
                new OutboxSubscriptionRegistrationOptions(),
                _ => subscription);
        return await ProcessNextBatchAsync(
            scope,
            subscription,
            registration,
            checkpointScope,
            ct);
    }

    internal async Task<int> ProcessNextBatchAsync(
        IServiceScope scope,
        IOutboxSubscription subscription,
        OutboxSubscriptionRegistration registration,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var reader = (EntityOutboxReader<TDbContext>)scope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var name = registration.Name;
        var startedAt = timeProvider.GetTimestamp();
        using var activity = EventStoreDaemonDiagnostics.StartBatch(name, "outbox-subscription");

        var checkpoint = await dbContext.Set<DbOutboxSubscription>().FirstOrDefaultAsync(
            row =>
                row.SubscriptionAssemblyQualifiedName == name &&
                row.CheckpointScope == checkpointScope.Scope &&
                row.TenantId == checkpointScope.TenantId,
            ct);

        if (checkpoint is null)
        {
            checkpoint = new DbOutboxSubscription
            {
                SubscriptionAssemblyQualifiedName = name,
                CheckpointScope = checkpointScope.Scope,
                TenantId = checkpointScope.TenantId
            };
            dbContext.Add(checkpoint);
        }

        if (!CanProcess(checkpoint))
        {
            await dbContext.SaveChangesAsync(ct);
            if (checkpoint.State == SubscriptionState.Paused)
            {
                serviceProvider.GetService<DaemonHealthMonitor>()?
                    .Heartbeat(name, "outbox-subscription");
            }
            return 0;
        }

        if (checkpoint.State == SubscriptionState.Faulted)
        {
            EventStoreDaemonDiagnostics.Retry(name, "outbox-subscription");
        }

        var query = dbContext.Set<DbOutboxMessage>()
            .AsNoTracking()
            .Where(message => message.Sequence > checkpoint.Sequence);

        if (checkpointScope.IsTenant)
        {
            query = query.Where(message => message.TenantId == checkpointScope.TenantId);
        }

        var messages = await query
            .OrderBy(message => message.Sequence)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(ct);

        var processed = 0;
        foreach (var message in messages)
        {
            if (!registration.Options.MatchesPersisted(message))
            {
                Complete(checkpoint, message.Sequence);
                processed++;
                await dbContext.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                checkpoint.LastAttemptAt = timeProvider.GetUtcNow();
                IOutboxEvent materialized;
                try
                {
                    materialized = reader.Materialize(message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (registration.Options.UnknownEventPolicy == UnknownEventPolicy.Skip)
                    {
                        Complete(checkpoint, message.Sequence);
                        processed++;
                        await dbContext.SaveChangesAsync(ct);
                        continue;
                    }

                    if (registration.Options.UnknownEventPolicy == UnknownEventPolicy.Custom)
                    {
                        var handler = registration.Options.UnknownEventHandler
                            ?? throw new InvalidOperationException(
                                "The custom unknown outbox-event policy has no handler.");
                        await handler(CreateUnknownContext(message, ex), ct);
                        Complete(checkpoint, message.Sequence);
                        processed++;
                        await dbContext.SaveChangesAsync(ct);
                        continue;
                    }

                    PersistFailure(checkpoint, message.Sequence, ex);
                    if (registration.Options.UnknownEventPolicy == UnknownEventPolicy.Quarantine)
                    {
                        checkpoint.State = SubscriptionState.DeadLettered;
                        checkpoint.NextAttemptAt = null;
                    }
                    await dbContext.SaveChangesAsync(ct);
                    EventStoreDaemonDiagnostics.Failed(name, "outbox-subscription");
                    await PublishFaultAsync(
                        name,
                        checkpoint,
                        checkpointScope,
                        ex,
                        ct);
                    return processed;
                }

                if (!registration.Options.MatchesMaterialized(materialized.EventType))
                {
                    Complete(checkpoint, message.Sequence);
                    processed++;
                    await dbContext.SaveChangesAsync(ct);
                    continue;
                }

                await subscription.Handle(materialized, ct);

                Complete(checkpoint, message.Sequence);
                processed++;

                await dbContext.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                PersistFailure(checkpoint, message.Sequence, ex);
                await dbContext.SaveChangesAsync(ct);
                EventStoreDaemonDiagnostics.Failed(name, "outbox-subscription");
                await PublishFaultAsync(
                    name,
                    checkpoint,
                    checkpointScope,
                    ex,
                    ct);
                return processed;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        var lag = await dbContext.Set<DbOutboxMessage>()
            .AsNoTracking()
            .Where(message => message.Sequence > checkpoint.Sequence)
            .Where(message =>
                !checkpointScope.IsTenant ||
                message.TenantId == checkpointScope.TenantId)
            .LongCountAsync(ct);
        EventStoreDaemonDiagnostics.BatchCompleted(
            name,
            "outbox-subscription",
            processed,
            timeProvider.GetElapsedTime(startedAt),
            Math.Max(0, lag));
        serviceProvider.GetService<DaemonHealthMonitor>()?
            .Heartbeat(name, "outbox-subscription");
        return processed;
    }

    private async Task<IReadOnlyList<CheckpointScopeKey>> GetCheckpointScopesAsync(
        OutboxSubscriptionRegistration registration,
        CancellationToken ct)
    {
        if (_options.CheckpointScope == CheckpointScope.Global)
        {
            return [CheckpointScopeKey.Global];
        }

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var name = registration.Name;

        var messageTenants = await dbContext.Set<DbOutboxMessage>()
            .AsNoTracking()
            .Select(message => message.TenantId)
            .Distinct()
            .ToListAsync(ct);
        var checkpointTenants = await dbContext.Set<DbOutboxSubscription>()
            .AsNoTracking()
            .Where(row =>
                row.SubscriptionAssemblyQualifiedName == name &&
                row.CheckpointScope == CheckpointScope.Tenant)
            .Select(row => row.TenantId)
            .Distinct()
            .ToListAsync(ct);

        return messageTenants
            .Concat(checkpointTenants)
            .Distinct()
            .OrderBy(tenantId => tenantId)
            .Select(CheckpointScopeKey.Tenant)
            .ToArray();
    }

    private async Task<IAsyncDisposable?> AcquireLockAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var lockName = $"entity-outbox:{subscriptionName}{checkpointScope.LockSuffix}";
        try
        {
            var acquired = await distributedLockProvider.AcquireLockAsync(lockName, _options.LockTimeout, ct);
            return acquired as IAsyncDisposable ?? acquired;
        }
        catch (TimeoutException)
        {
            EventStoreDaemonDiagnostics.LockContended(
                subscriptionName,
                "outbox-subscription");
            logger.LogDebug("Could not acquire entity outbox lock {LockName}.", lockName);
            return null;
        }
    }

    private bool CanProcess(DbOutboxSubscription checkpoint)
    {
        return checkpoint.State switch
        {
            SubscriptionState.Paused or SubscriptionState.DeadLettered => false,
            SubscriptionState.Faulted when checkpoint.NextAttemptAt > timeProvider.GetUtcNow() => false,
            _ => true
        };
    }

    private void PersistFailure(DbOutboxSubscription checkpoint, long sequence, Exception exception)
    {
        checkpoint.AttemptCount++;
        checkpoint.LastAttemptAt = timeProvider.GetUtcNow();
        checkpoint.NextAttemptAt = checkpoint.LastAttemptAt.Value.Add(_options.RetryDelay);
        checkpoint.LastError = exception.ToString();
        checkpoint.FailedEventSequence = sequence;
        checkpoint.State = checkpoint.AttemptCount >= _options.MaxRetryAttempts
            ? SubscriptionState.DeadLettered
            : SubscriptionState.Faulted;
    }

    private static void Complete(DbOutboxSubscription checkpoint, long sequence)
    {
        checkpoint.Sequence = sequence;
        checkpoint.State = SubscriptionState.Active;
        checkpoint.LastError = null;
        checkpoint.AttemptCount = 0;
        checkpoint.LastAttemptAt = null;
        checkpoint.NextAttemptAt = null;
        checkpoint.FailedEventSequence = null;
    }

    private static UnknownOutboxEventContext CreateUnknownContext(
        DbOutboxMessage message,
        Exception exception) =>
        new(
            message.EventId,
            message.Sequence,
            message.TypeName,
            message.Type,
            message.Data,
            message.Timestamp,
            message.TenantId,
            message.SourceEntityType,
            message.SourceEntityKey,
            message.ChangeKind,
            exception);

    private async ValueTask PublishFaultAsync(
        string identity,
        DbOutboxSubscription checkpoint,
        CheckpointScopeKey checkpointScope,
        Exception exception,
        CancellationToken ct)
    {
        var notification = new DaemonFaultNotification(
            identity,
            "outbox-subscription",
            checkpoint.State.ToString(),
            checkpointScope.Scope,
            checkpointScope.IsTenant ? checkpointScope.TenantId : null,
            checkpoint.FailedEventSequence,
            exception,
            timeProvider.GetUtcNow());

        foreach (var observer in serviceProvider.GetServices<IDaemonFaultObserver>())
        {
            try
            {
                await observer.OnFaultAsync(notification, ct);
            }
            catch (Exception observerException)
            {
                logger.LogWarning(
                    observerException,
                    "Daemon fault observer {ObserverType} failed for outbox subscription {Subscription}",
                    observer.GetType(),
                    identity);
            }
        }

        serviceProvider.GetService<DaemonHealthMonitor>()?
            .Fault(identity, "outbox-subscription", exception);
    }
}
