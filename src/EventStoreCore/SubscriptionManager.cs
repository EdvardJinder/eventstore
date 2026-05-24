using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EventStoreCore;

/// <summary>
/// Implementation of <see cref="ISubscriptionManager"/> for managing subscription state and replay operations.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
public sealed class SubscriptionManager<TDbContext> : ISubscriptionManager
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IReadOnlyList<string> _subscriptionNames;
    private readonly ILogger<SubscriptionManager<TDbContext>> _logger;

    /// <summary>
    /// Creates a new subscription manager.
    /// </summary>
    /// <param name="dbContext">The DbContext used for subscription state.</param>
    /// <param name="lockProvider">The distributed lock provider.</param>
    /// <param name="subscriptions">Registered subscriptions.</param>
    /// <param name="logger">The logger instance.</param>
    internal SubscriptionManager(
        TDbContext dbContext,
        IDistributedLockProvider lockProvider,
        IEnumerable<ISubscription> subscriptions,
        ILogger<SubscriptionManager<TDbContext>> logger)
    {
        _dbContext = dbContext;
        _lockProvider = lockProvider;
        _subscriptionNames = subscriptions
            .Select(subscription => subscription.GetType().AssemblyQualifiedName!)
            .Distinct()
            .ToList();
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SubscriptionStatusDto?> GetStatusAsync(string subscriptionName, CancellationToken ct = default)
    {
        return GetStatusAsync(subscriptionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task<SubscriptionStatusDto?> GetStatusAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        return GetStatusAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task<SubscriptionStatusDto?> GetStatusAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var record = await FindStatusAsync(subscriptionName, checkpointScope, asNoTracking: true)
            .FirstOrDefaultAsync(ct);

        if (record == null)
        {
            if (!_subscriptionNames.Contains(subscriptionName))
            {
                return null;
            }

            var totalEvents = await CountEventsAsync(checkpointScope, ct);
            return CreateDefaultStatus(subscriptionName, checkpointScope, totalEvents);
        }

        return await ToStatusDtoAsync(record, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var records = await _dbContext.Set<DbSubscription>()
            .AsNoTracking()
            .ToListAsync(ct);

        var result = new List<SubscriptionStatusDto>();

        foreach (var record in records)
        {
            result.Add(await ToStatusDtoAsync(record, ct));
        }

        var globalTotalEvents = await CountEventsAsync(CheckpointScopeKey.Global, ct);
        foreach (var subscriptionName in _subscriptionNames)
        {
            if (!records.Any(r =>
                r.SubscriptionAssemblyQualifiedName == subscriptionName &&
                r.CheckpointScope == CheckpointScope.Global &&
                r.TenantId == Guid.Empty))
            {
                result.Add(CreateDefaultStatus(subscriptionName, CheckpointScopeKey.Global, globalTotalEvents));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionStatusDto>> GetAllStatusesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var checkpointScope = CheckpointScopeKey.Tenant(tenantId);
        var records = await _dbContext.Set<DbSubscription>()
            .AsNoTracking()
            .Where(s =>
                s.CheckpointScope == checkpointScope.Scope &&
                s.TenantId == checkpointScope.TenantId)
            .ToListAsync(ct);

        var result = new List<SubscriptionStatusDto>();

        foreach (var record in records)
        {
            result.Add(await ToStatusDtoAsync(record, ct));
        }

        var totalEvents = await CountEventsAsync(checkpointScope, ct);
        foreach (var subscriptionName in _subscriptionNames)
        {
            if (!records.Any(r => r.SubscriptionAssemblyQualifiedName == subscriptionName))
            {
                result.Add(CreateDefaultStatus(subscriptionName, checkpointScope, totalEvents));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public Task PauseAsync(string subscriptionName, CancellationToken ct = default)
    {
        return PauseAsync(subscriptionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task PauseAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        return PauseAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task PauseAsync(string subscriptionName, CheckpointScopeKey checkpointScope, CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(subscriptionName, checkpointScope, ct);

        if (status.State is SubscriptionState.Faulted or SubscriptionState.DeadLettered)
        {
            throw new InvalidOperationException("Cannot pause a faulted or dead-lettered subscription. Retry or skip the failed event first.");
        }

        if (status.State == SubscriptionState.Paused)
        {
            throw new InvalidOperationException("Subscription is already paused.");
        }

        status.State = SubscriptionState.Paused;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Paused subscription {Subscription} in checkpoint scope {Scope}",
            subscriptionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task ResumeAsync(string subscriptionName, CancellationToken ct = default)
    {
        return ResumeAsync(subscriptionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task ResumeAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        return ResumeAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task ResumeAsync(string subscriptionName, CheckpointScopeKey checkpointScope, CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(subscriptionName, checkpointScope, ct);

        if (status.State != SubscriptionState.Paused)
        {
            throw new InvalidOperationException($"Subscription is not paused. Current state: {status.State}");
        }

        status.State = SubscriptionState.Active;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Resumed subscription {Subscription} in checkpoint scope {Scope}",
            subscriptionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task RetryFailedEventAsync(string subscriptionName, CancellationToken ct = default)
    {
        return RetryFailedEventAsync(subscriptionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task RetryFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        return RetryFailedEventAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task RetryFailedEventAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(subscriptionName, checkpointScope, ct);

        if (status.State is not (SubscriptionState.Faulted or SubscriptionState.DeadLettered))
        {
            throw new InvalidOperationException($"Subscription is not faulted. Current state: {status.State}");
        }

        ResetFailureState(status);
        status.State = SubscriptionState.Active;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Retrying failed event for subscription {Subscription} in checkpoint scope {Scope}",
            subscriptionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task SkipFailedEventAsync(string subscriptionName, CancellationToken ct = default)
    {
        return SkipFailedEventAsync(subscriptionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task SkipFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        return SkipFailedEventAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task SkipFailedEventAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(subscriptionName, checkpointScope, ct);

        if (status.State is not (SubscriptionState.Faulted or SubscriptionState.DeadLettered) || !status.FailedEventSequence.HasValue)
        {
            throw new InvalidOperationException("Subscription is not faulted or has no failed event to skip.");
        }

        status.Sequence = status.FailedEventSequence.Value;
        ResetFailureState(status);
        status.State = SubscriptionState.Active;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Skipped failed event at sequence {Sequence} for subscription {Subscription} in checkpoint scope {Scope}",
            status.Sequence,
            subscriptionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task<SubscriptionFailedEventDto?> GetFailedEventAsync(string subscriptionName, CancellationToken ct = default)
    {
        return GetFailedEventAsync(subscriptionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task<SubscriptionFailedEventDto?> GetFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        return GetFailedEventAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task<SubscriptionFailedEventDto?> GetFailedEventAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await FindStatusAsync(subscriptionName, checkpointScope, asNoTracking: true)
            .FirstOrDefaultAsync(ct);

        if (status?.State is not (SubscriptionState.Faulted or SubscriptionState.DeadLettered) || !status.FailedEventSequence.HasValue)
        {
            return null;
        }

        var dbEvent = await ApplyCheckpointScope(_dbContext.Events, checkpointScope)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Sequence == status.FailedEventSequence, ct);

        if (dbEvent == null)
        {
            return null;
        }

        var eventTypeName = string.IsNullOrWhiteSpace(dbEvent.TypeName)
            ? dbEvent.Type
            : dbEvent.TypeName;

        return new SubscriptionFailedEventDto(
            dbEvent.EventId,
            dbEvent.StreamId,
            dbEvent.Version,
            dbEvent.Sequence,
            eventTypeName,
            dbEvent.Data,
            dbEvent.Timestamp,
            status.LastError ?? "Unknown error")
        {
            CheckpointScope = checkpointScope.Scope,
            TenantId = checkpointScope.IsTenant ? checkpointScope.TenantId : null
        };
    }

    /// <inheritdoc />
    public Task ReplayAsync(
        string subscriptionName,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default)
    {
        return ReplayAsync(subscriptionName, CheckpointScopeKey.Global, startSequence, fromTimestamp, ct);
    }

    /// <inheritdoc />
    public Task ReplayAsync(
        string subscriptionName,
        Guid tenantId,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default)
    {
        return ReplayAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), startSequence, fromTimestamp, ct);
    }

    private async Task ReplayAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        long? startSequence,
        DateTimeOffset? fromTimestamp,
        CancellationToken ct)
    {
        if (startSequence.HasValue && fromTimestamp.HasValue)
        {
            throw new InvalidOperationException("Provide either startSequence or fromTimestamp, not both.");
        }

        if (!_subscriptionNames.Contains(subscriptionName))
        {
            throw new InvalidOperationException($"Subscription '{subscriptionName}' is not registered.");
        }

        var lockName = $"{subscriptionName}{checkpointScope.LockSuffix}";
        await using var lockHandle = await _lockProvider.AcquireLockAsync(lockName, cancellationToken: ct);

        var record = await FindStatusAsync(subscriptionName, checkpointScope, asNoTracking: false)
            .FirstOrDefaultAsync(ct);

        if (record == null)
        {
            record = new DbSubscription
            {
                SubscriptionAssemblyQualifiedName = subscriptionName,
                CheckpointScope = checkpointScope.Scope,
                TenantId = checkpointScope.TenantId,
                Sequence = 0
            };
            _dbContext.Set<DbSubscription>().Add(record);
        }

        var targetSequence = await ResolveReplayPositionAsync(checkpointScope, startSequence, fromTimestamp, ct);
        record.Sequence = targetSequence;
        record.State = SubscriptionState.Active;
        ResetFailureState(record);

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reset subscription {Subscription} in checkpoint scope {Scope} to sequence {Sequence} for replay",
            subscriptionName,
            checkpointScope,
            record.Sequence);
    }

    private async Task<long> ResolveReplayPositionAsync(
        CheckpointScopeKey checkpointScope,
        long? startSequence,
        DateTimeOffset? fromTimestamp,
        CancellationToken ct)
    {
        if (startSequence.HasValue)
        {
            return Math.Max(startSequence.Value - 1, 0);
        }

        if (fromTimestamp.HasValue)
        {
            var firstSequence = await ApplyCheckpointScope(_dbContext.Events, checkpointScope)
                .AsNoTracking()
                .Where(e => e.Timestamp >= fromTimestamp.Value)
                .OrderBy(e => e.Sequence)
                .Select(e => (long?)e.Sequence)
                .FirstOrDefaultAsync(ct);

            if (firstSequence.HasValue)
            {
                return Math.Max(firstSequence.Value - 1, 0);
            }

            return await ApplyCheckpointScope(_dbContext.Events, checkpointScope)
                .MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
        }

        return 0;
    }

    private async Task<DbSubscription> GetExistingStatusAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        return await FindStatusAsync(subscriptionName, checkpointScope, asNoTracking: false)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Subscription '{subscriptionName}' not found.");
    }

    private IQueryable<DbSubscription> FindStatusAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        bool asNoTracking)
    {
        var query = _dbContext.Set<DbSubscription>()
            .Where(s =>
                s.SubscriptionAssemblyQualifiedName == subscriptionName &&
                s.CheckpointScope == checkpointScope.Scope &&
                s.TenantId == checkpointScope.TenantId);

        return asNoTracking ? query.AsNoTracking() : query;
    }

    private async Task<SubscriptionStatusDto> ToStatusDtoAsync(DbSubscription record, CancellationToken ct)
    {
        var checkpointScope = new CheckpointScopeKey(record.CheckpointScope, record.TenantId);
        var totalEvents = await CountEventsAsync(checkpointScope, ct);
        var lastProcessedAt = await GetLastProcessedAtAsync(record.Sequence, checkpointScope, ct);
        var processedEvents = checkpointScope.IsTenant
            ? await CountProcessedEventsAsync(record.Sequence, checkpointScope, ct)
            : (long?)null;

        return record.ToDto(totalEvents, lastProcessedAt, processedEvents);
    }

    private static SubscriptionStatusDto CreateDefaultStatus(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        long totalEvents)
    {
        return new SubscriptionStatusDto(
            subscriptionName,
            0,
            SubscriptionState.Active,
            totalEvents,
            CalculateProgress(0, totalEvents),
            null,
            null,
            0,
            null,
            null,
            null)
        {
            CheckpointScope = checkpointScope.Scope,
            TenantId = checkpointScope.IsTenant ? checkpointScope.TenantId : null
        };
    }

    private static void ResetFailureState(DbSubscription status)
    {
        status.LastError = null;
        status.AttemptCount = 0;
        status.LastAttemptAt = null;
        status.NextAttemptAt = null;
        status.FailedEventSequence = null;
    }

    /// <summary>
    /// Calculates progress percentage from position and total event count.
    /// </summary>
    /// <param name="position">The last processed position.</param>
    /// <param name="totalEvents">The total number of events.</param>
    /// <returns>The progress percentage or null when total is unknown.</returns>
    private static double? CalculateProgress(long position, long totalEvents)
    {
        if (totalEvents <= 0)
        {
            return null;
        }

        return Math.Round((double)position / totalEvents * 100, 2);
    }

    private Task<long> CountEventsAsync(CheckpointScopeKey checkpointScope, CancellationToken ct)
    {
        return ApplyCheckpointScope(_dbContext.Events, checkpointScope).LongCountAsync(ct);
    }

    private Task<long> CountProcessedEventsAsync(
        long position,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        if (position <= 0)
        {
            return Task.FromResult(0L);
        }

        return ApplyCheckpointScope(_dbContext.Events, checkpointScope)
            .LongCountAsync(e => e.Sequence <= position, ct);
    }

    private async Task<DateTimeOffset?> GetLastProcessedAtAsync(
        long position,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        if (position <= 0)
        {
            return null;
        }

        return await ApplyCheckpointScope(_dbContext.Events, checkpointScope)
            .AsNoTracking()
            .Where(e => e.Sequence == position)
            .Select(e => (DateTimeOffset?)e.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    private static IQueryable<DbEvent> ApplyCheckpointScope(
        IQueryable<DbEvent> query,
        CheckpointScopeKey checkpointScope)
    {
        return checkpointScope.IsTenant
            ? query.Where(e => e.TenantId == checkpointScope.TenantId)
            : query;
    }
}
