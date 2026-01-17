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
    public async Task<SubscriptionStatusDto?> GetStatusAsync(string subscriptionName, CancellationToken ct = default)
    {
        var record = await _dbContext.Set<DbSubscription>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SubscriptionAssemblyQualifiedName == subscriptionName, ct);

        if (record == null)
        {
            if (!_subscriptionNames.Contains(subscriptionName))
            {
                return null;
            }

            var totalEvents = await _dbContext.Events.LongCountAsync(ct);
            return new SubscriptionStatusDto(
                subscriptionName,
                0,
                totalEvents,
                CalculateProgress(0, totalEvents),
                null);
        }

        var lastProcessedAt = await GetLastProcessedAtAsync(record.Sequence, ct);
        var totalCount = await _dbContext.Events.LongCountAsync(ct);

        return new SubscriptionStatusDto(
            record.SubscriptionAssemblyQualifiedName,
            record.Sequence,
            totalCount,
            CalculateProgress(record.Sequence, totalCount),
            lastProcessedAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var records = await _dbContext.Set<DbSubscription>()
            .AsNoTracking()
            .ToListAsync(ct);

        var totalEvents = await _dbContext.Events.LongCountAsync(ct);
        var lastProcessedLookup = await GetLastProcessedLookupAsync(records, ct);

        var result = new List<SubscriptionStatusDto>();

        foreach (var record in records)
        {
            lastProcessedLookup.TryGetValue(record.Sequence, out var lastProcessedAt);

            result.Add(new SubscriptionStatusDto(
                record.SubscriptionAssemblyQualifiedName,
                record.Sequence,
                totalEvents,
                CalculateProgress(record.Sequence, totalEvents),
                lastProcessedAt));
        }

        foreach (var subscriptionName in _subscriptionNames)
        {
            if (!records.Any(r => r.SubscriptionAssemblyQualifiedName == subscriptionName))
            {
                result.Add(new SubscriptionStatusDto(
                    subscriptionName,
                    0,
                    totalEvents,
                    CalculateProgress(0, totalEvents),
                    null));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task ReplayAsync(
        string subscriptionName,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default)
    {
        if (startSequence.HasValue && fromTimestamp.HasValue)
        {
            throw new InvalidOperationException("Provide either startSequence or fromTimestamp, not both.");
        }

        if (!_subscriptionNames.Contains(subscriptionName))
        {
            throw new InvalidOperationException($"Subscription '{subscriptionName}' is not registered.");
        }

        await using var lockHandle = await _lockProvider.AcquireLockAsync(subscriptionName, cancellationToken: ct);

        var record = await _dbContext.Set<DbSubscription>()
            .FirstOrDefaultAsync(s => s.SubscriptionAssemblyQualifiedName == subscriptionName, ct);

        if (record == null)
        {
            record = new DbSubscription
            {
                SubscriptionAssemblyQualifiedName = subscriptionName,
                Sequence = 0
            };
            _dbContext.Set<DbSubscription>().Add(record);
        }

        var targetSequence = await ResolveReplayPositionAsync(startSequence, fromTimestamp, ct);
        record.Sequence = targetSequence;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reset subscription {Subscription} to sequence {Sequence} for replay",
            subscriptionName,
            record.Sequence);
    }

    /// <summary>
    /// Resolves the sequence position for a replay request.
    /// </summary>
    /// <param name="startSequence">Explicit start sequence, if provided.</param>
    /// <param name="fromTimestamp">Timestamp to start from, if provided.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sequence position to persist.</returns>
    private async Task<long> ResolveReplayPositionAsync(
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
            var firstSequence = await _dbContext.Events
                .AsNoTracking()
                .Where(e => e.Timestamp >= fromTimestamp.Value)
                .OrderBy(e => e.Sequence)
                .Select(e => (long?)e.Sequence)
                .FirstOrDefaultAsync(ct);

            if (firstSequence.HasValue)
            {
                return Math.Max(firstSequence.Value - 1, 0);
            }

            var maxSequence = await _dbContext.Events.MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
            return maxSequence;
        }

        return 0;
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

    /// <summary>
    /// Gets the timestamp of the last processed event at a given position.
    /// </summary>
    /// <param name="position">The event sequence position.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The timestamp of the event, or null when not available.</returns>
    private async Task<DateTimeOffset?> GetLastProcessedAtAsync(long position, CancellationToken ct)
    {
        if (position <= 0)
        {
            return null;
        }

        return await _dbContext.Events
            .AsNoTracking()
            .Where(e => e.Sequence == position)
            .Select(e => (DateTimeOffset?)e.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Builds a lookup for last processed timestamps by sequence number.
    /// </summary>
    /// <param name="records">Subscription records to inspect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A lookup of sequence to timestamp.</returns>
    private async Task<Dictionary<long, DateTimeOffset>> GetLastProcessedLookupAsync(
        IReadOnlyCollection<DbSubscription> records,
        CancellationToken ct)
    {
        var positions = records
            .Select(r => r.Sequence)
            .Where(sequence => sequence > 0)
            .Distinct()
            .ToArray();

        if (positions.Length == 0)
        {
            return new Dictionary<long, DateTimeOffset>();
        }

        return await _dbContext.Events
            .AsNoTracking()
            .Where(e => positions.Contains(e.Sequence))
            .ToDictionaryAsync(e => e.Sequence, e => e.Timestamp, ct);
    }
}

