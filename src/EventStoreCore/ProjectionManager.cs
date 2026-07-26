using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IServiceProvider _services;
    private readonly ProjectionDaemonOptions _options;

    /// <summary>
    /// Creates a new projection manager.
    /// </summary>
    /// <param name="dbContext">The DbContext used for projection state.</param>
    /// <param name="lockProvider">The distributed lock provider.</param>
    /// <param name="projections">Registered projection metadata.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="services">The active application service scope.</param>
    /// <param name="options">Projection daemon configuration.</param>
    internal ProjectionManager(
        TDbContext dbContext,
        IDistributedLockProvider lockProvider,
        IEnumerable<ProjectionRegistration> projections,
        ILogger<ProjectionManager<TDbContext>> logger,
        IServiceProvider services,
        IOptions<ProjectionDaemonOptions> options)
    {
        _dbContext = dbContext;
        _lockProvider = lockProvider;
        _projections = projections;
        _logger = logger;
        _services = services;
        _options = options.Value;
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

        return await ToStatusDtoAsync(status, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var statuses = await _dbContext.Set<DbProjectionStatus>()
            .AsNoTracking()
            .ToListAsync(ct);

        var result = new List<ProjectionStatusDto>();
        foreach (var status in statuses)
        {
            result.Add(await ToStatusDtoAsync(status, ct));
        }

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

        var result = new List<ProjectionStatusDto>();
        foreach (var status in statuses)
        {
            result.Add(await ToStatusDtoAsync(status, ct));
        }

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
    public Task RebuildAsync(string projectionName, CancellationToken ct = default)
    {
        return RebuildAsync(projectionName, tenantId: null, ct);
    }

    /// <inheritdoc />
    public Task RebuildAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        return RebuildAsync(projectionName, (Guid?)tenantId, ct);
    }

    private async Task RebuildAsync(string projectionName, Guid? tenantId, CancellationToken ct)
    {
        var registration = _projections.FirstOrDefault(p => p.Name == projectionName)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered.");
        if (registration.Mode != ProjectionMode.Eventual)
        {
            throw new InvalidOperationException(
                $"Projection '{projectionName}' runs inline and cannot be rebuilt by the eventual projection daemon.");
        }

        if (tenantId.HasValue && _options.CheckpointScope != CheckpointScope.Tenant)
        {
            throw new InvalidOperationException(
                "A tenant id can only be supplied when projection checkpoints are tenant-scoped.");
        }

        if (_options.CheckpointScope == CheckpointScope.Tenant && !registration.Options.UsesShadowRebuilds)
        {
            throw new InvalidOperationException(
                $"Projection '{projectionName}' must configure UseShadowRebuilds() before tenant-scoped rebuilds can run. " +
                "ClearAsync is global and cannot safely rebuild one tenant.");
        }

        var coordinatorLockName = _options.CheckpointScope == CheckpointScope.Global
            ? $"projection:{projectionName}"
            : $"projection-rebuild:{projectionName}";
        await using var coordinatorLock = await _lockProvider.AcquireLockAsync(
            coordinatorLockName,
            cancellationToken: ct);

        var existingTenantScopeIds = _options.CheckpointScope == CheckpointScope.Tenant
            ? await _dbContext.Set<DbProjectionStatus>()
                .AsNoTracking()
                .Where(s =>
                    s.ProjectionName == projectionName &&
                    s.CheckpointScope == CheckpointScope.Tenant)
                .Select(s => s.TenantId)
                .Distinct()
                .ToListAsync(ct)
            : [];

        var eventTenantScopeIds = _options.CheckpointScope == CheckpointScope.Tenant
            ? await _dbContext.Events
                .AsNoTracking()
                .Select(e => e.TenantId)
                .Distinct()
                .ToListAsync(ct)
            : [];

        var tenantScopeIds = existingTenantScopeIds
            .Concat(eventTenantScopeIds)
            .Distinct()
            .OrderBy(tenantId => tenantId)
            .ToArray();

        var checkpointScopes = _options.CheckpointScope == CheckpointScope.Global
            ? [CheckpointScopeKey.Global]
            : tenantId.HasValue
                ? [CheckpointScopeKey.Tenant(tenantId.Value)]
                : tenantScopeIds.Select(CheckpointScopeKey.Tenant).ToArray();

        foreach (var checkpointScope in checkpointScopes)
        {
            var scopeLockName = $"projection:{projectionName}{checkpointScope.LockSuffix}";
            await using var scopeLock = checkpointScope.IsTenant
                ? await _lockProvider.AcquireLockAsync(scopeLockName, cancellationToken: ct)
                : null;

            var status = await GetOrCreateStatusAsync(
                projectionName,
                registration.Version,
                checkpointScope,
                ct);
            await InitiateRebuildAsync(registration, status, checkpointScope, ct);
        }
    }

    /// <inheritdoc />
    public Task CancelRebuildAsync(string projectionName, CancellationToken ct = default)
    {
        if (_options.CheckpointScope == CheckpointScope.Tenant)
        {
            throw new InvalidOperationException(
                "A tenant id is required to cancel a rebuild when projection checkpoints are tenant-scoped.");
        }

        return CancelRebuildAsync(projectionName, CheckpointScopeKey.Global, ct);
    }

    /// <inheritdoc />
    public Task CancelRebuildAsync(string projectionName, Guid tenantId, CancellationToken ct = default)
    {
        if (_options.CheckpointScope != CheckpointScope.Tenant)
        {
            throw new InvalidOperationException(
                "A tenant id can only be supplied when projection checkpoints are tenant-scoped.");
        }

        return CancelRebuildAsync(projectionName, CheckpointScopeKey.Tenant(tenantId), ct);
    }

    private async Task CancelRebuildAsync(
        string projectionName,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        var registration = _projections.FirstOrDefault(p => p.Name == projectionName)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered.");
        var lockName = $"projection:{projectionName}{checkpointScope.LockSuffix}";
        await using var lockHandle = await _lockProvider.AcquireLockAsync(lockName, cancellationToken: ct);
        var status = await GetExistingStatusAsync(projectionName, checkpointScope, ct);

        if (status.State != ProjectionState.Rebuilding || !status.RebuildId.HasValue)
        {
            throw new InvalidOperationException("Only an active shadow rebuild can be cancelled.");
        }

        var rebuild = CreateRebuild(status, registration);
        await registration.DiscardRebuildAction(_dbContext, _services, rebuild, ct);

        status.State = ProjectionState.Active;
        status.Position = status.RebuildPreviousPosition ?? status.Position;
        status.RebuildId = null;
        status.RebuildPreviousPosition = null;
        status.TotalEvents = null;
        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task InitiateRebuildAsync(
        ProjectionRegistration registration,
        DbProjectionStatus status,
        CheckpointScopeKey checkpointScope,
        CancellationToken ct)
    {
        if (status.State == ProjectionState.Rebuilding)
        {
            throw new InvalidOperationException(
                $"Projection '{registration.Name}' is already rebuilding in checkpoint scope {checkpointScope}.");
        }

        ProjectionRebuild? rebuild = null;
        if (registration.Options.UsesShadowRebuilds)
        {
            rebuild = new ProjectionRebuild(
                Guid.NewGuid(),
                registration.Version,
                checkpointScope.Scope,
                checkpointScope.IsTenant ? checkpointScope.TenantId : null);
            try
            {
                await registration.PrepareRebuildAction(_dbContext, _services, rebuild, ct);
                await _dbContext.SaveChangesAsync(ct);
            }
            catch
            {
                try
                {
                    await registration.DiscardRebuildAction(
                        _dbContext,
                        _services,
                        rebuild,
                        CancellationToken.None);
                }
                catch (Exception discardException)
                {
                    _logger.LogWarning(
                        discardException,
                        "Failed to discard shadow target {RebuildId} after rebuild preparation failed",
                        rebuild.Id);
                }
                throw;
            }
        }

        await ResetStatusForRebuildAsync(status, DateTimeOffset.UtcNow, rebuild?.Id, ct);
        await _dbContext.SaveChangesAsync(ct);

        if (rebuild is null)
        {
            await registration.ClearAction(_dbContext, _services, ct);
            await _dbContext.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Rebuild {RebuildId} initiated for projection {Projection} in checkpoint scope {Scope}",
            rebuild?.Id,
            registration.Name,
            checkpointScope);
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

    private async Task ResetStatusForRebuildAsync(
        DbProjectionStatus status,
        DateTimeOffset rebuildStartedAt,
        Guid? rebuildId,
        CancellationToken ct)
    {
        var checkpointScope = new CheckpointScopeKey(status.CheckpointScope, status.TenantId);

        status.State = ProjectionState.Rebuilding;
        status.RebuildPreviousPosition = rebuildId.HasValue ? status.Position : null;
        status.Position = 0;
        status.RebuildStartedAt = rebuildStartedAt;
        status.RebuildCompletedAt = null;
        status.RebuildId = rebuildId;
        status.LastError = null;
        status.FailedEventSequence = null;
        status.TotalEvents = await ApplyCheckpointScope(_dbContext.Events, checkpointScope)
            .LongCountAsync(ct);
    }

    private static ProjectionRebuild CreateRebuild(
        DbProjectionStatus status,
        ProjectionRegistration registration)
    {
        return new ProjectionRebuild(
            status.RebuildId
                ?? throw new InvalidOperationException("The shadow rebuild identifier is missing. Check the projection status migration."),
            registration.Version,
            status.CheckpointScope,
            status.CheckpointScope == CheckpointScope.Tenant ? status.TenantId : null);
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

    private async Task<ProjectionStatusDto> ToStatusDtoAsync(
        DbProjectionStatus status,
        CancellationToken ct)
    {
        var checkpointScope = new CheckpointScopeKey(status.CheckpointScope, status.TenantId);
        var events = ApplyCheckpointScope(_dbContext.Events.AsNoTracking(), checkpointScope);
        var totalEvents = await events.LongCountAsync(ct);
        var processedEvents = status.Position <= 0
            ? 0
            : await events.LongCountAsync(e => e.Sequence <= status.Position, ct);

        return status.ToDto(totalEvents, processedEvents);
    }
}
