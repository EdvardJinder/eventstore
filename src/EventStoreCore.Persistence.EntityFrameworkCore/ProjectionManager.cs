using EventStoreCore;
using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EventStoreCore.Persistence.EntityFrameworkCore;

/// <summary>
/// Implementation of IProjectionManager for managing projection state and operations.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
public sealed class ProjectionManager<TDbContext> : IProjectionManager
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IEnumerable<ProjectionRegistration> _projections;
    private readonly ILogger<ProjectionManager<TDbContext>> _logger;

    internal ProjectionManager(
        TDbContext dbContext,
        IDistributedLockProvider lockProvider,
        IEnumerable<ProjectionRegistration> projections,
        ILogger<ProjectionManager<TDbContext>> logger)
    {
        _dbContext = dbContext;
        _lockProvider = lockProvider;
        _projections = projections;
        _logger = logger;
    }

    public async Task<ProjectionStatusDto?> GetStatusAsync(string projectionName, CancellationToken ct = default)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct);

        if (status == null)
        {
            // Check if projection is registered but not yet initialized
            var registration = _projections.FirstOrDefault(p => p.Name == projectionName);
            if (registration != null)
            {
                // Return a default status for uninitialized projection
                return new ProjectionStatusDto(
                    projectionName,
                    registration.Version,
                    ProjectionState.Active,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                );
            }
            return null;
        }

        return status.ToDto();
    }

    public async Task<IReadOnlyList<ProjectionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var statuses = await _dbContext.Set<DbProjectionStatus>()
            .AsNoTracking()
            .ToListAsync(ct);

        var result = new List<ProjectionStatusDto>();

        // Add statuses for projections that have records
        foreach (var status in statuses)
        {
            result.Add(status.ToDto());
        }

        // Add default status for registered projections without records
        foreach (var registration in _projections)
        {
            if (!statuses.Any(s => s.ProjectionName == registration.Name))
            {
                result.Add(new ProjectionStatusDto(
                    registration.Name,
                    registration.Version,
                    ProjectionState.Active,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ));
            }
        }

        return result;
    }

    public async Task RebuildAsync(string projectionName, CancellationToken ct = default)
    {
        var registration = _projections.FirstOrDefault(p => p.Name == projectionName)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered.");

        var lockName = $"projection:{projectionName}";
        
        await using var lockHandle = await _lockProvider.AcquireLockAsync(lockName, cancellationToken: ct);

        _logger.LogInformation("Initiating manual rebuild for projection {Projection}", projectionName);

        var status = await GetOrCreateStatusAsync(projectionName, registration.Version, ct);

        // Update status to rebuilding
        status.State = ProjectionState.Rebuilding;
        status.Position = 0;
        status.Version = registration.Version;
        status.RebuildStartedAt = DateTimeOffset.UtcNow;
        status.RebuildCompletedAt = null;
        status.LastError = null;
        status.FailedEventSequence = null;
        status.TotalEvents = await _dbContext.Events.LongCountAsync(ct);

        await _dbContext.SaveChangesAsync(ct);

        // Clear projection data
        await registration.ClearAction(_dbContext, ct);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rebuild initiated for projection {Projection}, clearing data and replaying {Total} events",
            projectionName, status.TotalEvents);
    }

    public async Task PauseAsync(string projectionName, CancellationToken ct = default)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' not found.");

        if (status.State == ProjectionState.Faulted)
        {
            throw new InvalidOperationException("Cannot pause a faulted projection. Retry or skip the failed event first.");
        }

        status.State = ProjectionState.Paused;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Paused projection {Projection}", projectionName);
    }

    public async Task ResumeAsync(string projectionName, CancellationToken ct = default)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' not found.");

        if (status.State != ProjectionState.Paused)
        {
            throw new InvalidOperationException($"Projection is not paused. Current state: {status.State}");
        }

        status.State = ProjectionState.Active;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Resumed projection {Projection}", projectionName);
    }

    public async Task RetryFailedEventAsync(string projectionName, CancellationToken ct = default)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' not found.");

        if (status.State != ProjectionState.Faulted)
        {
            throw new InvalidOperationException($"Projection is not faulted. Current state: {status.State}");
        }

        // Clear error state and set back to active
        // The daemon will retry processing from the current position
        status.State = ProjectionState.Active;
        status.LastError = null;
        status.FailedEventSequence = null;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Retrying failed event for projection {Projection}", projectionName);
    }

    public async Task SkipFailedEventAsync(string projectionName, CancellationToken ct = default)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' not found.");

        if (status.State != ProjectionState.Faulted || !status.FailedEventSequence.HasValue)
        {
            throw new InvalidOperationException("Projection is not faulted or has no failed event to skip.");
        }

        // Skip the failed event by advancing position past it
        status.Position = status.FailedEventSequence.Value;
        status.State = ProjectionState.Active;
        status.LastError = null;
        status.FailedEventSequence = null;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Skipped failed event at sequence {Sequence} for projection {Projection}",
            status.Position, projectionName);
    }

    public async Task<FailedEventDto?> GetFailedEventAsync(string projectionName, CancellationToken ct = default)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct);

        if (status?.State != ProjectionState.Faulted || !status.FailedEventSequence.HasValue)
        {
            return null;
        }

        var dbEvent = await _dbContext.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Sequence == status.FailedEventSequence, ct);

        if (dbEvent == null)
        {
            return null;
        }

        return new FailedEventDto(
            dbEvent.EventId,
            dbEvent.StreamId,
            dbEvent.Version,
            dbEvent.Sequence,
            dbEvent.Type,
            dbEvent.Data,
            dbEvent.Timestamp,
            status.LastError ?? "Unknown error"
        );
    }

    private async Task<DbProjectionStatus> GetOrCreateStatusAsync(string projectionName, int version, CancellationToken ct)
    {
        var status = await _dbContext.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, ct);

        if (status == null)
        {
            status = new DbProjectionStatus
            {
                ProjectionName = projectionName,
                Version = version,
                State = ProjectionState.Active,
                Position = 0
            };
            _dbContext.Set<DbProjectionStatus>().Add(status);
            await _dbContext.SaveChangesAsync(ct);
        }

        return status;
    }
}
