using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EventStoreCore;


/// <summary>
/// Implementation of <see cref="IProjectionManager"/> for managing projection state and operations.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
public sealed class ProjectionManager<TDbContext> : IProjectionManager
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IEnumerable<ProjectionRegistration> _projections;
    private readonly ILogger<ProjectionManager<TDbContext>> _logger;

    /// <summary>
    /// Creates a new projection manager.
    /// </summary>
    /// <param name="dbContext">The DbContext used for projection state.</param>
    /// <param name="lockProvider">The distributed lock provider.</param>
    /// <param name="projections">Registered projection metadata.</param>
    /// <param name="logger">The logger instance.</param>
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

    /// <inheritdoc />
    public Task<ProjectionStatusDto?> GetStatusAsync(string projectionName, CancellationToken ct = default)
    {
        return GetStatusAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task<ProjectionStatusDto?> GetStatusAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return GetStatusAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task<ProjectionStatusDto?> GetStatusAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await FindStatusAsync(projectionName, checkpointScope, asNoTracking: true)
            .FirstOrDefaultAsync(ct);

        if (status == null)
        {
            var registration = _projections.FirstOrDefault(p => p.Name == projectionName);
            return registration == null
                ? null
                : CreateDefaultStatus(projectionName, registration.Version, checkpointScope);
        }

        return status.ToDto();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var statuses = await _dbContext.Set<DbProjectionStatus>()
            .AsNoTracking()
            .ToListAsync(ct);

        var result = statuses
            .Select(status => status.ToDto())
            .ToList();

        foreach (var registration in _projections)
        {
            if (!statuses.Any(s =>
                s.ProjectionName == registration.Name &&
                s.CheckpointScope == CheckpointScope.Global &&
                s.TenantId == Guid.Empty))
            {
                result.Add(CreateDefaultStatus(registration.Name, registration.Version, CheckpointScopeKey.Global));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectionStatusDto>> GetAllStatusesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var checkpointScope = CheckpointScopeKey.Tenant(tenantId);
        var statuses = await _dbContext.Set<DbProjectionStatus>()
            .AsNoTracking()
            .Where(s =>
                s.CheckpointScope == checkpointScope.Scope &&
                s.TenantId == checkpointScope.TenantId)
            .ToListAsync(ct);

        var result = statuses
            .Select(status => status.ToDto())
            .ToList();

        foreach (var registration in _projections)
        {
            if (!statuses.Any(s => s.ProjectionName == registration.Name))
            {
                result.Add(CreateDefaultStatus(registration.Name, registration.Version, checkpointScope));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task RebuildAsync(string projectionName, CancellationToken ct = default)
    {
        var registration = _projections.FirstOrDefault(p => p.Name == projectionName)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered.");

        var lockName = $"projection:{projectionName}";

        await using var lockHandle = await _lockProvider.AcquireLockAsync(lockName, cancellationToken: ct);

        _logger.LogInformation("Initiating manual rebuild for projection {Projection}", projectionName);

        var status = await GetOrCreateStatusAsync(projectionName, registration.Version, CheckpointScopeKey.Global, ct);

        status.State = ProjectionState.Rebuilding;
        status.Position = 0;
        status.Version = registration.Version;
        status.RebuildStartedAt = DateTimeOffset.UtcNow;
        status.RebuildCompletedAt = null;
        status.LastError = null;
        status.FailedEventSequence = null;
        status.TotalEvents = await _dbContext.Events.LongCountAsync(ct);

        await _dbContext.SaveChangesAsync(ct);

        await registration.ClearAction(_dbContext, ct);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rebuild initiated for projection {Projection}, clearing data and replaying {Total} events",
            projectionName, status.TotalEvents);
    }

    /// <inheritdoc />
    public Task PauseAsync(string projectionName, CancellationToken ct = default)
    {
        return PauseAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task PauseAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return PauseAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task PauseAsync(string projectionName, CheckpointScopeKey checkpointScope, CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(projectionName, checkpointScope, ct);

        if (status.State == ProjectionState.Faulted)
        {
            throw new InvalidOperationException("Cannot pause a faulted projection. Retry or skip the failed event first.");
        }

        status.State = ProjectionState.Paused;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Paused projection {Projection} in checkpoint scope {Scope}",
            projectionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task ResumeAsync(string projectionName, CancellationToken ct = default)
    {
        return ResumeAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task ResumeAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return ResumeAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task ResumeAsync(string projectionName, CheckpointScopeKey checkpointScope, CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(projectionName, checkpointScope, ct);

        if (status.State != ProjectionState.Paused)
        {
            throw new InvalidOperationException($"Projection is not paused. Current state: {status.State}");
        }

        status.State = ProjectionState.Active;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Resumed projection {Projection} in checkpoint scope {Scope}",
            projectionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task RetryFailedEventAsync(string projectionName, CancellationToken ct = default)
    {
        return RetryFailedEventAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task RetryFailedEventAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return RetryFailedEventAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task RetryFailedEventAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(projectionName, checkpointScope, ct);

        if (status.State != ProjectionState.Faulted)
        {
            throw new InvalidOperationException($"Projection is not faulted. Current state: {status.State}");
        }

        status.State = ProjectionState.Active;
        status.LastError = null;
        status.FailedEventSequence = null;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Retrying failed event for projection {Projection} in checkpoint scope {Scope}",
            projectionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task SkipFailedEventAsync(string projectionName, CancellationToken ct = default)
    {
        return SkipFailedEventAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task SkipFailedEventAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return SkipFailedEventAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task SkipFailedEventAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await GetExistingStatusAsync(projectionName, checkpointScope, ct);

        if (status.State != ProjectionState.Faulted || !status.FailedEventSequence.HasValue)
        {
            throw new InvalidOperationException("Projection is not faulted or has no failed event to skip.");
        }

        status.Position = status.FailedEventSequence.Value;
        status.State = ProjectionState.Active;
        status.LastError = null;
        status.FailedEventSequence = null;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Skipped failed event at sequence {Sequence} for projection {Projection} in checkpoint scope {Scope}",
            status.Position,
            projectionName,
            checkpointScope);
    }

    /// <inheritdoc />
    public Task<FailedEventDto?> GetFailedEventAsync(string projectionName, CancellationToken ct = default)
    {
        return GetFailedEventAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task<FailedEventDto?> GetFailedEventAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return GetFailedEventAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task<FailedEventDto?> GetFailedEventAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await FindStatusAsync(projectionName, checkpointScope, asNoTracking: true)
            .FirstOrDefaultAsync(ct);

        if (status?.State != ProjectionState.Faulted || !status.FailedEventSequence.HasValue)
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

        return new FailedEventDto(
            dbEvent.EventId,
            dbEvent.StreamId,
            dbEvent.Version,
            dbEvent.Sequence,
            eventTypeName,
            dbEvent.Data,
            dbEvent.Timestamp,
            status.LastError ?? "Unknown error"
        )
        {
            CheckpointScope = checkpointScope.Scope,
            TenantId = checkpointScope.IsTenant ? checkpointScope.TenantId : null
        };
    }

    private async Task<DbProjectionStatus> GetOrCreateStatusAsync(
        string projectionName,
        int version,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var status = await FindStatusAsync(projectionName, checkpointScope, asNoTracking: false)
            .FirstOrDefaultAsync(ct);

        if (status == null)
        {
            status = new DbProjectionStatus
            {
                ProjectionName = projectionName,
                CheckpointScope = checkpointScope.Scope,
                TenantId = checkpointScope.TenantId,
                Version = version,
                State = ProjectionState.Active,
                Position = 0
            };
            _dbContext.Set<DbProjectionStatus>().Add(status);
            await _dbContext.SaveChangesAsync(ct);
        }

        return status;
    }

    private async Task<DbProjectionStatus> GetExistingStatusAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        return await FindStatusAsync(projectionName, checkpointScope, asNoTracking: false)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' not found.");
    }

    private IQueryable<DbProjectionStatus> FindStatusAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        bool asNoTracking)
    {
        var query = _dbContext.Set<DbProjectionStatus>()
            .Where(s =>
                s.ProjectionName == projectionName &&
                s.CheckpointScope == checkpointScope.Scope &&
                s.TenantId == checkpointScope.TenantId);

        return asNoTracking ? query.AsNoTracking() : query;
    }

    private static ProjectionStatusDto CreateDefaultStatus(
        string projectionName,
        int version,
        CheckpointScopeKey checkpointScope)
    {
        return new ProjectionStatusDto(
            projectionName,
            version,
            ProjectionState.Active,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        )
        {
            CheckpointScope = checkpointScope.Scope,
            TenantId = checkpointScope.IsTenant ? checkpointScope.TenantId : null
        };
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
