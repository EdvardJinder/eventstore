using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EventStoreCore;

/// <summary>
/// Manages entity-outbox subscription status, recovery, and replay.
/// </summary>
/// <typeparam name="TDbContext">The DbContext containing the outbox tables.</typeparam>
public sealed class EntityOutboxManager<TDbContext> : IOutboxSubscriptionManager
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IReadOnlySet<string> _subscriptionNames;
    private readonly ILogger<EntityOutboxManager<TDbContext>> _logger;

    internal EntityOutboxManager(
        TDbContext dbContext,
        IDistributedLockProvider lockProvider,
        IEnumerable<OutboxSubscriptionRegistration> registrations,
        ILogger<EntityOutboxManager<TDbContext>> logger)
    {
        _dbContext = dbContext;
        _lockProvider = lockProvider;
        _subscriptionNames = registrations
            .Select(registration => registration.Name)
            .ToHashSet(StringComparer.Ordinal);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<OutboxSubscriptionStatusDto?> GetStatusAsync(
        string subscriptionName,
        CancellationToken ct = default) =>
        GetStatusAsync(subscriptionName, CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task<OutboxSubscriptionStatusDto?> GetStatusAsync(
        string subscriptionName,
        Guid tenantId,
        CancellationToken ct = default) =>
        GetStatusAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);

    private async Task<OutboxSubscriptionStatusDto?> GetStatusAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var record = await FindStatus(subscriptionName, checkpointScope, true)
            .FirstOrDefaultAsync(ct);
        if (record is not null)
        {
            return await ToStatusDtoAsync(record, ct);
        }

        if (!_subscriptionNames.Contains(subscriptionName))
        {
            return null;
        }

        return CreateDefaultStatus(
            subscriptionName,
            checkpointScope,
            await CountEventsAsync(checkpointScope, ct));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxSubscriptionStatusDto>> GetAllStatusesAsync(
        CancellationToken ct = default) =>
        GetAllStatusesAsync(CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxSubscriptionStatusDto>> GetAllStatusesAsync(
        Guid tenantId,
        CancellationToken ct = default) =>
        GetAllStatusesAsync(CheckpointScopeKey.Tenant(tenantId), ct);

    private async Task<IReadOnlyList<OutboxSubscriptionStatusDto>> GetAllStatusesAsync(
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var records = await _dbContext.Set<DbOutboxSubscription>()
            .AsNoTracking()
            .Where(record =>
                record.CheckpointScope == checkpointScope.Scope &&
                record.TenantId == checkpointScope.TenantId)
            .ToListAsync(ct);
        var result = new List<OutboxSubscriptionStatusDto>(records.Count + _subscriptionNames.Count);

        foreach (var record in records)
        {
            result.Add(await ToStatusDtoAsync(record, ct));
        }

        var totalEvents = await CountEventsAsync(checkpointScope, ct);
        foreach (var subscriptionName in _subscriptionNames)
        {
            if (records.All(record => record.SubscriptionAssemblyQualifiedName != subscriptionName))
            {
                result.Add(CreateDefaultStatus(subscriptionName, checkpointScope, totalEvents));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public Task PauseAsync(string subscriptionName, CancellationToken ct = default) =>
        PauseAsync(subscriptionName, CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task PauseAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default) =>
        PauseAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);

    private Task PauseAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct) =>
        MutateAsync(subscriptionName, checkpointScope, status =>
        {
            if (status.State is SubscriptionState.Faulted or SubscriptionState.DeadLettered)
            {
                throw new InvalidOperationException(
                    "Cannot pause a faulted or dead-lettered outbox subscription. Retry or skip the failed event first.");
            }

            if (status.State == SubscriptionState.Paused)
            {
                throw new InvalidOperationException("Outbox subscription is already paused.");
            }

            status.State = SubscriptionState.Paused;
        }, "Paused", ct);

    /// <inheritdoc />
    public Task ResumeAsync(string subscriptionName, CancellationToken ct = default) =>
        ResumeAsync(subscriptionName, CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task ResumeAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default) =>
        ResumeAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);

    private Task ResumeAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct) =>
        MutateAsync(subscriptionName, checkpointScope, status =>
        {
            if (status.State != SubscriptionState.Paused)
            {
                throw new InvalidOperationException(
                    $"Outbox subscription is not paused. Current state: {status.State}");
            }

            status.State = SubscriptionState.Active;
        }, "Resumed", ct);

    /// <inheritdoc />
    public Task RetryFailedEventAsync(string subscriptionName, CancellationToken ct = default) =>
        RetryFailedEventAsync(subscriptionName, CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task RetryFailedEventAsync(
        string subscriptionName,
        Guid tenantId,
        CancellationToken ct = default) =>
        RetryFailedEventAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);

    private Task RetryFailedEventAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct) =>
        MutateAsync(subscriptionName, checkpointScope, status =>
        {
            if (status.State is not (SubscriptionState.Faulted or SubscriptionState.DeadLettered))
            {
                throw new InvalidOperationException(
                    $"Outbox subscription is not faulted. Current state: {status.State}");
            }

            ResetFailureState(status);
            status.State = SubscriptionState.Active;
        }, "Retrying failed event for", ct);

    /// <inheritdoc />
    public Task SkipFailedEventAsync(string subscriptionName, CancellationToken ct = default) =>
        SkipFailedEventAsync(subscriptionName, CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task SkipFailedEventAsync(
        string subscriptionName,
        Guid tenantId,
        CancellationToken ct = default) =>
        SkipFailedEventAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);

    private Task SkipFailedEventAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct) =>
        MutateAsync(subscriptionName, checkpointScope, status =>
        {
            if (status.State is not (SubscriptionState.Faulted or SubscriptionState.DeadLettered) ||
                !status.FailedEventSequence.HasValue)
            {
                throw new InvalidOperationException(
                    "Outbox subscription is not faulted or has no failed event to skip.");
            }

            status.Sequence = status.FailedEventSequence.Value;
            ResetFailureState(status);
            status.State = SubscriptionState.Active;
        }, "Skipped failed event for", ct);

    /// <inheritdoc />
    public Task<OutboxFailedEventDto?> GetFailedEventAsync(
        string subscriptionName,
        CancellationToken ct = default) =>
        GetFailedEventAsync(subscriptionName, CheckpointScopeKey.Global, ct);

    /// <inheritdoc />
    public Task<OutboxFailedEventDto?> GetFailedEventAsync(
        string subscriptionName,
        Guid tenantId,
        CancellationToken ct = default) =>
        GetFailedEventAsync(subscriptionName, CheckpointScopeKey.Tenant(tenantId), ct);

    private async Task<OutboxFailedEventDto?> GetFailedEventAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await FindStatus(subscriptionName, checkpointScope, true)
            .FirstOrDefaultAsync(ct);
        if (status?.State is not (SubscriptionState.Faulted or SubscriptionState.DeadLettered) ||
            !status.FailedEventSequence.HasValue)
        {
            return null;
        }

        var message = await ApplyCheckpointScope(
                _dbContext.Set<DbOutboxMessage>().AsNoTracking(),
                checkpointScope)
            .FirstOrDefaultAsync(
                candidate => candidate.Sequence == status.FailedEventSequence.Value,
                ct);
        if (message is null)
        {
            return null;
        }

        return new OutboxFailedEventDto(
            message.EventId,
            message.Sequence,
            string.IsNullOrWhiteSpace(message.TypeName) ? message.Type : message.TypeName,
            message.Data,
            message.Timestamp,
            message.TenantId,
            message.SourceEntityType,
            message.SourceEntityKey,
            message.ChangeKind,
            status.LastError ?? "Unknown error")
        {
            CheckpointScope = checkpointScope.Scope
        };
    }

    /// <inheritdoc />
    public Task ReplayAsync(
        string subscriptionName,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default) =>
        ReplayAsync(
            subscriptionName,
            CheckpointScopeKey.Global,
            startSequence,
            fromTimestamp,
            ct);

    /// <inheritdoc />
    public Task ReplayAsync(
        string subscriptionName,
        Guid tenantId,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default) =>
        ReplayAsync(
            subscriptionName,
            CheckpointScopeKey.Tenant(tenantId),
            startSequence,
            fromTimestamp,
            ct);

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

        if (startSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startSequence),
                "The replay start sequence cannot be negative.");
        }

        EnsureRegistered(subscriptionName);
        await using var lockHandle = await AcquireLockAsync(subscriptionName, checkpointScope, ct);
        var status = await FindStatus(subscriptionName, checkpointScope, false)
            .FirstOrDefaultAsync(ct);
        if (status is null)
        {
            status = new DbOutboxSubscription
            {
                SubscriptionAssemblyQualifiedName = subscriptionName,
                CheckpointScope = checkpointScope.Scope,
                TenantId = checkpointScope.TenantId
            };
            _dbContext.Add(status);
        }

        status.Sequence = await ResolveReplayPositionAsync(
            checkpointScope,
            startSequence,
            fromTimestamp,
            ct);
        status.State = SubscriptionState.Active;
        ResetFailureState(status);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reset outbox subscription {Subscription} in checkpoint scope {Scope} to sequence {Sequence}",
            subscriptionName,
            checkpointScope,
            status.Sequence);
    }

    private async Task MutateAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        Action<DbOutboxSubscription> mutate,
        string action,
        CancellationToken ct)
    {
        EnsureRegistered(subscriptionName);
        await using var lockHandle = await AcquireLockAsync(subscriptionName, checkpointScope, ct);
        var status = await FindStatus(subscriptionName, checkpointScope, false)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Outbox subscription '{subscriptionName}' was not initialized.");

        mutate(status);
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation(
            "{Action} outbox subscription {Subscription} in checkpoint scope {Scope}",
            action,
            subscriptionName,
            checkpointScope);
    }

    private ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct) =>
        _lockProvider.AcquireLockAsync(
            $"entity-outbox:{subscriptionName}{checkpointScope.LockSuffix}",
            cancellationToken: ct);

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

        var messages = ApplyCheckpointScope(_dbContext.Set<DbOutboxMessage>(), checkpointScope);
        if (fromTimestamp.HasValue)
        {
            var firstSequence = await messages
                .AsNoTracking()
                .Where(message => message.Timestamp >= fromTimestamp.Value)
                .OrderBy(message => message.Sequence)
                .Select(message => (long?)message.Sequence)
                .FirstOrDefaultAsync(ct);
            return firstSequence.HasValue
                ? Math.Max(firstSequence.Value - 1, 0)
                : await messages.MaxAsync(message => (long?)message.Sequence, ct) ?? 0;
        }

        return 0;
    }

    private IQueryable<DbOutboxSubscription> FindStatus(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        bool asNoTracking)
    {
        var query = _dbContext.Set<DbOutboxSubscription>()
            .Where(status =>
                status.SubscriptionAssemblyQualifiedName == subscriptionName &&
                status.CheckpointScope == checkpointScope.Scope &&
                status.TenantId == checkpointScope.TenantId);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    private async Task<OutboxSubscriptionStatusDto> ToStatusDtoAsync(
        DbOutboxSubscription record,
        CancellationToken ct)
    {
        var checkpointScope = new CheckpointScopeKey(record.CheckpointScope, record.TenantId);
        var totalEvents = await CountEventsAsync(checkpointScope, ct);
        var processedEvents = record.Sequence <= 0
            ? 0
            : await ApplyCheckpointScope(
                    _dbContext.Set<DbOutboxMessage>(),
                    checkpointScope)
                .LongCountAsync(message => message.Sequence <= record.Sequence, ct);
        var lastProcessedAt = record.Sequence <= 0
            ? null
            : await ApplyCheckpointScope(
                    _dbContext.Set<DbOutboxMessage>(),
                    checkpointScope)
                .Where(message => message.Sequence == record.Sequence)
                .Select(message => (DateTimeOffset?)message.Timestamp)
                .FirstOrDefaultAsync(ct);

        return new OutboxSubscriptionStatusDto(
            record.SubscriptionAssemblyQualifiedName,
            record.Sequence,
            record.State,
            totalEvents,
            CalculateProgress(processedEvents, totalEvents),
            lastProcessedAt,
            record.LastError,
            record.AttemptCount,
            record.LastAttemptAt,
            record.NextAttemptAt,
            record.FailedEventSequence)
        {
            CheckpointScope = checkpointScope.Scope,
            TenantId = checkpointScope.IsTenant ? checkpointScope.TenantId : null
        };
    }

    private static OutboxSubscriptionStatusDto CreateDefaultStatus(
        string subscriptionName,
        CheckpointScopeKey checkpointScope,
        long totalEvents) =>
        new(
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

    private Task<long> CountEventsAsync(CheckpointScopeKey checkpointScope, CancellationToken ct) =>
        ApplyCheckpointScope(_dbContext.Set<DbOutboxMessage>(), checkpointScope)
            .LongCountAsync(ct);

    private static IQueryable<DbOutboxMessage> ApplyCheckpointScope(
        IQueryable<DbOutboxMessage> messages,
        CheckpointScopeKey checkpointScope) =>
        checkpointScope.IsTenant
            ? messages.Where(message => message.TenantId == checkpointScope.TenantId)
            : messages;

    private static double? CalculateProgress(long processedEvents, long totalEvents) =>
        totalEvents <= 0
            ? null
            : Math.Round((double)processedEvents / totalEvents * 100, 2);

    private static void ResetFailureState(DbOutboxSubscription status)
    {
        status.LastError = null;
        status.AttemptCount = 0;
        status.LastAttemptAt = null;
        status.NextAttemptAt = null;
        status.FailedEventSequence = null;
    }

    private void EnsureRegistered(string subscriptionName)
    {
        if (!_subscriptionNames.Contains(subscriptionName))
        {
            throw new InvalidOperationException(
                $"Outbox subscription '{subscriptionName}' is not registered.");
        }
    }
}
