using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStoreCore;


/// <summary>
/// Background service that processes async projections and handles rebuilds.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
public sealed class ProjectionDaemon<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly ILogger<ProjectionDaemon<TDbContext>> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDistributedLockProvider _distributedLockProvider;
    private readonly ProjectionDaemonOptions _options;
    private readonly IReadOnlyList<ProjectionRegistration> _projections;

    internal IReadOnlyList<ProjectionRegistration> Projections => _projections;

    /// <summary>
    /// Creates a new projection daemon.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceProvider">Service provider for resolving scoped services.</param>
    /// <param name="distributedLockProvider">Distributed lock provider.</param>
    /// <param name="options">Daemon options.</param>
    public ProjectionDaemon(
        ILogger<ProjectionDaemon<TDbContext>> logger,
        IServiceProvider serviceProvider,
        IDistributedLockProvider distributedLockProvider,
        IOptions<ProjectionDaemonOptions> options)

    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _distributedLockProvider = distributedLockProvider;
        _options = options.Value;
        _projections = serviceProvider.GetServices<ProjectionRegistration>().ToList();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Projection daemon starting with {Count} registered projections", _projections.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var projection in _projections)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessProjectionAsync(projection, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Projection daemon stopping gracefully");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing projection {Projection}", projection.Name);
                    await Task.Delay(_options.RetryDelay, stoppingToken);
                }
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
        }
    }


    /// <summary>
    /// Processes a single projection, including rebuild logic.
    /// </summary>
    /// <param name="projection">The projection registration.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task ProcessProjectionAsync(ProjectionRegistration projection, CancellationToken ct)
    {
        var lockName = $"projection:{projection.Name}";


        IDistributedSynchronizationHandle? lockHandle = null;
        try
        {
            lockHandle = await _distributedLockProvider.TryAcquireLockAsync(
                lockName,
                TimeSpan.FromSeconds(2),
                ct);

            if (lockHandle == null)
            {
                _logger.LogDebug("Could not acquire lock for projection {Projection}, another instance may be processing", projection.Name);
                return;
            }

            _logger.LogDebug("Acquired lock for projection {Projection}", projection.Name);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var status = await GetOrCreateStatusAsync(dbContext, projection, ct);

            // Check for version mismatch and trigger rebuild if configured
            if (_options.AutoRebuildOnVersionChange && status.Version != projection.Version)
            {
                _logger.LogInformation(
                    "Projection {Projection} version changed from {OldVersion} to {NewVersion}, triggering rebuild",
                    projection.Name, status.Version, projection.Version);

                await InitiateRebuildAsync(dbContext, projection, status, ct);
            }

            // Process based on current state
            switch (status.State)
            {
                case ProjectionState.Active:
                    await ProcessEventsAsync(dbContext, projection, status, ct);
                    break;

                case ProjectionState.Rebuilding:
                    await ContinueRebuildAsync(dbContext, projection, status, ct);
                    break;

                case ProjectionState.Paused:
                    _logger.LogDebug("Projection {Projection} is paused, skipping", projection.Name);
                    break;

                case ProjectionState.Faulted:
                    _logger.LogDebug("Projection {Projection} is faulted, requires manual intervention", projection.Name);
                    break;
            }
        }
        finally
        {
            if (lockHandle != null)
            {
                await lockHandle.DisposeAsync();
                _logger.LogDebug("Released lock for projection {Projection}", projection.Name);
            }
        }
    }

    /// <summary>
    /// Gets or creates the projection status record.
    /// </summary>
    /// <param name="dbContext">The DbContext used for persistence.</param>
    /// <param name="projection">The projection registration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The projection status record.</returns>
    private async Task<DbProjectionStatus> GetOrCreateStatusAsync(
        TDbContext dbContext,
        ProjectionRegistration projection,
        CancellationToken ct)
    {
        var status = await dbContext.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(s => s.ProjectionName == projection.Name, ct);


        if (status == null)
        {
            status = new DbProjectionStatus
            {
                ProjectionName = projection.Name,
                Version = projection.Version,
                State = ProjectionState.Active,
                Position = 0
            };
            dbContext.Set<DbProjectionStatus>().Add(status);
            await dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Created projection status for {Projection}", projection.Name);
        }

        return status;
    }

    /// <summary>
    /// Initiates a projection rebuild by clearing data and resetting status.
    /// </summary>
    /// <param name="dbContext">The DbContext used for persistence.</param>
    /// <param name="projection">The projection registration.</param>
    /// <param name="status">The current projection status.</param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task InitiateRebuildAsync(
        TDbContext dbContext,
        ProjectionRegistration projection,
        DbProjectionStatus status,
        CancellationToken ct)
    {
        _logger.LogInformation("Initiating rebuild for projection {Projection}", projection.Name);


        // Update status to rebuilding
        status.State = ProjectionState.Rebuilding;
        status.Position = 0;
        status.Version = projection.Version;
        status.RebuildStartedAt = DateTimeOffset.UtcNow;
        status.RebuildCompletedAt = null;
        status.LastError = null;
        status.FailedEventSequence = null;

        // Get total events for progress tracking
        status.TotalEvents = await dbContext.Events.LongCountAsync(ct);

        await dbContext.SaveChangesAsync(ct);

        // Clear projection data
        _logger.LogInformation("Clearing data for projection {Projection}", projection.Name);
        await projection.ClearAction(dbContext, ct);
        await dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Rebuild initiated for projection {Projection}, replaying {Total} events", 
            projection.Name, status.TotalEvents);
    }

    /// <summary>
    /// Continues a rebuild by processing the next batch of events.
    /// </summary>
    /// <param name="dbContext">The DbContext used for persistence.</param>
    /// <param name="projection">The projection registration.</param>
    /// <param name="status">The current projection status.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task ContinueRebuildAsync(
        TDbContext dbContext,
        ProjectionRegistration projection,
        DbProjectionStatus status,
        CancellationToken ct)
    {
        var processed = await ProcessBatchAsync(dbContext, projection, status, ct);


        if (!processed)
        {
            // Rebuild complete
            status.State = ProjectionState.Active;
            status.RebuildCompletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Rebuild completed for projection {Projection} in {Duration}",
                projection.Name,
                status.RebuildCompletedAt - status.RebuildStartedAt);
        }
    }

    /// <summary>
    /// Processes new events for an active projection.
    /// </summary>
    /// <param name="dbContext">The DbContext used for persistence.</param>
    /// <param name="projection">The projection registration.</param>
    /// <param name="status">The current projection status.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task ProcessEventsAsync(
        TDbContext dbContext,
        ProjectionRegistration projection,
        DbProjectionStatus status,
        CancellationToken ct)
    {
        var processed = await ProcessBatchAsync(dbContext, projection, status, ct);


        if (!processed)
        {
            _logger.LogDebug("Projection {Projection} is caught up at position {Position}", 
                projection.Name, status.Position);
        }
    }

    /// <summary>
    /// Processes a batch of events for a projection.
    /// </summary>
    /// <param name="dbContext">The DbContext used for persistence.</param>
    /// <param name="projection">The projection registration.</param>
    /// <param name="status">The current projection status.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when events were processed; false when no events were available.</returns>
    private async Task<bool> ProcessBatchAsync(
        TDbContext dbContext,
        ProjectionRegistration projection,
        DbProjectionStatus status,
        CancellationToken ct)
    {
        var events = await dbContext.Events
            .Where(e => e.Sequence > status.Position)
            .OrderBy(e => e.Sequence)
            .Take(_options.BatchSize)
            .ToListAsync(ct);


        if (events.Count == 0)
        {
            return false;
        }

        _logger.LogDebug(
            "Processing batch of {Count} events for projection {Projection} starting from sequence {Sequence}",
            events.Count, projection.Name, events[0].Sequence);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var dbEvent in events)
            {
                var @event = dbEvent.ToEvent();

                // Check if this event type is handled by the projection
                if (!projection.Options.IsHandeled(@event.EventType))
                {
                    status.Position = dbEvent.Sequence;
                    continue;
                }

                // Get the key for this event
                var keySelector = projection.Options.GetKeySelector(@event.EventType);
                var key = keySelector((IEvent<object>)@event);

                // Get or create the snapshot
                var snapshot = await projection.GetOrCreateSnapshotAction(dbContext, key, ct);
                var isNew = dbContext.Entry(snapshot).State == EntityState.Detached;

                // Apply the event
                await projection.EvolveAction(dbContext, _serviceProvider, snapshot, @event, ct);

                // Add snapshot if it was new
                if (isNew)
                {
                    projection.AddSnapshotAction(dbContext, snapshot);
                }

                status.Position = dbEvent.Sequence;
                status.LastProcessedAt = DateTimeOffset.UtcNow;
            }

            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogDebug(
                "Processed batch for projection {Projection}, new position: {Position}",
                projection.Name, status.Position);

            // Optional batch delay for throttling
            if (_options.BatchDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.BatchDelay, ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing event at sequence {Sequence} for projection {Projection}",
                status.Position + 1, projection.Name);

            // Mark projection as faulted
            status.State = ProjectionState.Faulted;
            status.LastError = ex.ToString();
            status.FailedEventSequence = events.FirstOrDefault(e => e.Sequence > status.Position)?.Sequence;

            // Save status in a new context to avoid transaction issues
            using var errorScope = _serviceProvider.CreateScope();
            var errorContext = errorScope.ServiceProvider.GetRequiredService<TDbContext>();
            var errorStatus = await errorContext.Set<DbProjectionStatus>()
                .FirstAsync(s => s.ProjectionName == projection.Name, ct);
            
            errorStatus.State = ProjectionState.Faulted;
            errorStatus.LastError = ex.ToString();
            errorStatus.FailedEventSequence = status.FailedEventSequence;
            await errorContext.SaveChangesAsync(ct);

            throw;
        }
    }
}
