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
        var subscriptions = serviceProvider.GetServices<IOutboxSubscription>().ToArray();

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;

            foreach (var subscription in subscriptions)
            {
                try
                {
                    foreach (var checkpointScope in await GetCheckpointScopesAsync(subscription, stoppingToken))
                    {
                        await using var acquired = await AcquireLockAsync(subscription, checkpointScope, stoppingToken);
                        if (acquired is null)
                        {
                            continue;
                        }

                        using var scope = serviceProvider.CreateScope();
                        processedAny |= await ProcessNextBatchAsync(
                            scope,
                            subscription,
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
                    logger.LogError(ex, "Entity outbox subscription {Subscription} failed.", GetName(subscription));
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
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var reader = (EntityOutboxReader<TDbContext>)scope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var name = GetName(subscription);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
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
            await transaction.CommitAsync(ct);
            return 0;
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
            try
            {
                checkpoint.LastAttemptAt = timeProvider.GetUtcNow();
                await subscription.Handle(reader.Materialize(message), ct);

                checkpoint.Sequence = message.Sequence;
                checkpoint.State = SubscriptionState.Active;
                checkpoint.LastError = null;
                checkpoint.AttemptCount = 0;
                checkpoint.NextAttemptAt = null;
                checkpoint.FailedEventSequence = null;
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
                await transaction.CommitAsync(ct);
                return processed;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return processed;
    }

    private async Task<IReadOnlyList<CheckpointScopeKey>> GetCheckpointScopesAsync(
        IOutboxSubscription subscription,
        CancellationToken ct)
    {
        if (_options.CheckpointScope == CheckpointScope.Global)
        {
            return [CheckpointScopeKey.Global];
        }

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var name = GetName(subscription);

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
        IOutboxSubscription subscription,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var lockName = $"entity-outbox:{GetName(subscription)}{checkpointScope.LockSuffix}";
        try
        {
            var acquired = await distributedLockProvider.AcquireLockAsync(lockName, _options.LockTimeout, ct);
            return acquired as IAsyncDisposable ?? acquired;
        }
        catch (TimeoutException)
        {
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

    private static string GetName(IOutboxSubscription subscription)
        => subscription.GetType().AssemblyQualifiedName
            ?? throw new InvalidOperationException("An outbox subscription type has no assembly-qualified name.");
}
