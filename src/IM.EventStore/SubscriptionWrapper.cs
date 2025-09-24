using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IM.EventStore;

internal class SubscriptionWrapper<TDbContext, TSubscription> : BackgroundService
    where TDbContext : DbContext
    where TSubscription : ISubscription
{
    private readonly IDbContextFactory<TDbContext> _dbFactory;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SubscriptionOptions _options;

    private readonly string _subscriptionType;
    private readonly string _nodeId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public SubscriptionWrapper(
        IDbContextFactory<TDbContext> dbFactory,
        IServiceScopeFactory serviceScopeFactory,
        IOptionsFactory<SubscriptionOptions> options)
    {
        _dbFactory = dbFactory;
        _subscriptionType = GetType().FullName ?? GetType().Name;
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Create($"{typeof(TDbContext).Name}{typeof(TSubscription).Name}");
    }

    // --- Public DSL ---------------------------------------------------------

    // Your handler
    internal async Task OnEventAsync(IEvent @event, CancellationToken ct)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var subscription = scope.ServiceProvider.GetRequiredService<TSubscription>();
        await subscription.OnEventAsync(@event, ct);
    }

    // --- Tunables -----------------------------------------------------------

    protected virtual int BatchSize => 512;
    protected virtual TimeSpan IdleDelay => TimeSpan.FromMilliseconds(500);
    protected virtual TimeSpan LeaseDuration => TimeSpan.FromSeconds(30);
    protected virtual TimeSpan ErrorBackoff => TimeSpan.FromSeconds(3);

    // --- Runtime ------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);

                var sub = await EnsureSubscriptionRowAsync(db, stoppingToken);

                if (!await TryAcquireLeaseAsync(db, sub.Id, _nodeId, stoppingToken))
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                try
                {
                    await RunLoopAsync(db, sub, stoppingToken);
                }
                finally
                {
                    await ReleaseLeaseAsync(db, sub.Id, _nodeId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // TODO: replace with your logger
                Console.Error.WriteLine(ex);
                await Task.Delay(ErrorBackoff, stoppingToken);
            }
        }
    }

    internal async Task<DbSubscription> EnsureSubscriptionRowAsync(TDbContext db, CancellationToken ct)
    {
        var sub = await db.Set<DbSubscription>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubscriptionType == _subscriptionType, ct);
        if (sub is null)
        {
            sub = new DbSubscription
            {
                Id = Guid.NewGuid(),
                SubscriptionType = _subscriptionType,
                CurrentSequence = 0
            };
            db.Set<DbSubscription>().Add(sub);
            await db.SaveChangesAsync(ct);

            if (_options.StartFromPresent)
            {
                // start after the newest event at creation time
                var last = await db.Set<DbEvent>().AsNoTracking().MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
                sub.CurrentSequence = last;
                await db.SaveChangesAsync(ct);
            }
            else if (_options.StartFromTimestamp is DateTimeOffset ts)
            {
                var seq = await db.Set<DbEvent>()
                    .AsNoTracking()
                    .Where(e => e.Timestamp <= ts)
                    .MaxAsync(e => (long?)e.Sequence, ct) ?? 0;

                sub.CurrentSequence = seq;
                await db.SaveChangesAsync(ct);
            }
        }
        return sub;
    }

    internal async Task RunLoopAsync(TDbContext db, DbSubscription sub, CancellationToken ct)
    {
        long lastProcessedSeq = sub.CurrentSequence;
        while (true)
        {
            await RenewLeaseAsync(db, sub.Id, _nodeId, ct);

            // Build the base query
            IQueryable<DbEvent> q = db.Set<DbEvent>().AsNoTracking()
                .Where(e => e.Sequence > lastProcessedSeq);

            var batch = await q
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                await Task.Delay(IdleDelay, ct);
                return; // give up lease to be polite
            }

            foreach (var dbEvent in batch)
            {
                // Optional double-check (if you filtered above, this is always true)
                if (!_options.HandlesAll && _options.Types.Count > 0 && !_options.Types.Contains(Type.GetType(dbEvent.Type)))
                    continue;

                Type? eventType = Type.GetType(dbEvent.Type);
                if (eventType is null)
                {
                    lastProcessedSeq = dbEvent.Sequence; // still advance past it
                    continue;
                }
                var type = typeof(Event<>).MakeGenericType(eventType);
                IEvent evt = (IEvent)Activator.CreateInstance(type, dbEvent)!;
                if (evt is null) // unknown type or intentionally ignored
                {
                    lastProcessedSeq = dbEvent.Sequence; // still advance past it
                    continue;
                }

                // User code must be idempotent (at-least-once semantics)
                await OnEventAsync(evt, ct);
                lastProcessedSeq = dbEvent.Sequence;
            }

            // Atomically advance progress in a short transaction
            var rows = await db.Database.ExecuteSqlRawAsync("""
                UPDATE subscriptions
                   SET current_sequence = GREATEST(current_sequence, @seq)
                 WHERE id = @id
                   AND lease_owner = @nodeId
                """,
                parameters: [
                    new Npgsql.NpgsqlParameter("seq", lastProcessedSeq),
                    new Npgsql.NpgsqlParameter("id", sub.Id),
                    new Npgsql.NpgsqlParameter("nodeId", _nodeId)
                ], 
                ct
            );

            if (rows == 0)
            {
                // lost the lease or someone moved the row; just exit and retry next loop
                return;
            }


        }
    }

    // --- Leasing (row-lease) -----------------------------------------------

    internal async Task<bool> TryAcquireLeaseAsync(TDbContext db, Guid subId, string nodeId, CancellationToken ct)
    {
        var sql = """
            UPDATE subscriptions
               SET lease_owner = @nodeId,
                   lease_expires_utc = (now() at time zone 'utc') + @lease
             WHERE id = @id
               AND (lease_owner IS NULL
                    OR lease_expires_utc < (now() at time zone 'utc')
                    OR lease_owner = @nodeId)
            """;

        var rows = await db.Database.ExecuteSqlRawAsync(sql,
            cancellationToken: ct, parameters: [
            new Npgsql.NpgsqlParameter("nodeId", nodeId),
            new Npgsql.NpgsqlParameter("lease", LeaseDuration),
            new Npgsql.NpgsqlParameter("id", subId)]);

        return rows > 0;
    }

    internal async Task<bool> RenewLeaseAsync(TDbContext db, Guid subId, string nodeId, CancellationToken ct)
    {
        var sql = """
            UPDATE subscriptions
               SET lease_expires_utc = (now() at time zone 'utc') + @lease
             WHERE id = @id AND lease_owner = @nodeId
            """;
        var result = await db.Database.ExecuteSqlRawAsync(sql,
            parameters: [
            new Npgsql.NpgsqlParameter("lease", LeaseDuration),
            new Npgsql.NpgsqlParameter("id", subId),
            new Npgsql.NpgsqlParameter("nodeId", nodeId)], ct);

        return result > 0;
    }

    internal async Task<bool> ReleaseLeaseAsync(TDbContext db, Guid subId, string nodeId, CancellationToken ct)
    {
        var sql = """
            UPDATE subscriptions
               SET lease_owner = NULL, lease_expires_utc = NULL
             WHERE id = @id AND lease_owner = @nodeId
            """;
        var result = await db.Database.ExecuteSqlRawAsync(sql,
            parameters: [
            new Npgsql.NpgsqlParameter("id", subId),
            new Npgsql.NpgsqlParameter("nodeId", nodeId)
            ], ct);
        return result > 0;
    }
}

