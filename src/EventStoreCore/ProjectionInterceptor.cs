using System.Runtime.CompilerServices;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

/// <summary>
/// Intercepts EF Core SaveChanges to execute inline projections.
/// </summary>
/// <typeparam name="TProjection">The projection implementation.</typeparam>
/// <typeparam name="TSnapshot">The snapshot type.</typeparam>
public sealed class ProjectionInterceptor<TProjection, TSnapshot> : SaveChangesInterceptor
    where TProjection : IProjection<TSnapshot>, new()
    where TSnapshot : class, new()
{
    private static readonly ConditionalWeakTable<DbContext, List<DbEvent>> PendingEvents = new();
    private static readonly AsyncLocal<bool> SuppressStatusTracking = new();

    private readonly ProjectionOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _projectionName;
    private readonly int _projectionVersion;

    /// <summary>
    /// Creates a new projection interceptor.
    /// </summary>
    /// <param name="options">Projection options.</param>
    /// <param name="serviceProvider">Service provider used to resolve dependencies.</param>
    /// <param name="projectionVersion">The projection version for status tracking.</param>
    public ProjectionInterceptor(ProjectionOptions options, IServiceProvider serviceProvider, int projectionVersion)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _projectionName = typeof(TProjection).FullName ?? typeof(TProjection).Name;
        _projectionVersion = projectionVersion;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (SuppressStatusTracking.Value)
        {
            return result;
        }

        if (eventData.Context is not DbContext db)
        {
            return result;
        }

        // Check if this projection is currently rebuilding - if so, skip inline processing
        // The projection daemon will handle all events during rebuild
        if (await IsProjectionRebuildingAsync(db, cancellationToken))
        {
            return result;
        }

        var streams = db.ChangeTracker.Entries<DbStream>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        var handledEvents = new List<DbEvent>();

        foreach (var stream in streams)
        {
            var events = db.ChangeTracker.Entries<DbEvent>()
                .Where(e => e.State is EntityState.Added && e.Entity.StreamId == stream.Entity.Id)
                .OrderBy(e => e.Entity.Version)
                .Select(e => new { Entry = e, Event = e.Entity.ToEvent() })
                .Where(e => _options.IsHandeled(e.Event.EventType))
                .ToArray();

            if (events is not { Length: > 0 })
            {
                continue;
            }

            using var scope = _serviceProvider.CreateScope();
            var projectionContext = new ProjectionContext(db, scope.ServiceProvider);

            foreach (var item in events.OrderBy(e => e.Event.Version))
            {
                var keySelector = _options.GetKeySelector(item.Event.EventType);

                var key = keySelector((IEvent<object>)item.Event);

                var snaphost = await db
                    .Set<TSnapshot>()
                    .FindAsync([key], cancellationToken);

                if (snaphost is null)
                {
                    snaphost = new TSnapshot();
                    await TProjection.Evolve(snaphost, item.Event, projectionContext, cancellationToken);
                    db.Add(snaphost);
                }
                else
                {
                    await TProjection.Evolve(snaphost, item.Event, projectionContext, cancellationToken);
                }

                handledEvents.Add(item.Entry.Entity);
            }
        }

        if (handledEvents.Count > 0)
        {
            var pending = PendingEvents.GetOrCreateValue(db);
            pending.AddRange(handledEvents);
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (SuppressStatusTracking.Value)
        {
            return result;
        }

        if (eventData.Context is not DbContext db)
        {
            return result;
        }

        if (!PendingEvents.TryGetValue(db, out var pendingEvents) || pendingEvents.Count == 0)
        {
            return result;
        }

        PendingEvents.Remove(db);

        if (await IsProjectionRebuildingAsync(db, cancellationToken))
        {
            return result;
        }

        var maxSequence = pendingEvents.Max(e => e.Sequence);
        if (maxSequence <= 0)
        {
            return result;
        }

        try
        {
            var statusSet = db.Set<DbProjectionStatus>();
            var status = await statusSet
                .FirstOrDefaultAsync(s => s.ProjectionName == _projectionName, cancellationToken);

            if (status == null)
            {
                status = new DbProjectionStatus
                {
                    ProjectionName = _projectionName,
                    Version = _projectionVersion,
                    State = ProjectionState.Active,
                    Position = maxSequence,
                    LastProcessedAt = DateTimeOffset.UtcNow
                };
                statusSet.Add(status);
            }
            else
            {
                status.Version = _projectionVersion;
                status.State = ProjectionState.Active;
                status.Position = Math.Max(status.Position, maxSequence);
                status.LastProcessedAt = DateTimeOffset.UtcNow;
                status.LastError = null;
                status.FailedEventSequence = null;
            }

            SuppressStatusTracking.Value = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // DbProjectionStatus entity is not configured in this context
            // This is expected if the user hasn't set up the projection daemon
        }
        finally
        {
            SuppressStatusTracking.Value = false;
        }

        return result;
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is DbContext db)
        {
            PendingEvents.Remove(db);
        }

        return Task.CompletedTask;
    }

    private async Task<bool> IsProjectionRebuildingAsync(DbContext db, CancellationToken ct)
    {
        try
        {
            var status = await db.Set<DbProjectionStatus>()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ProjectionName == _projectionName, ct);

            return status?.State == ProjectionState.Rebuilding;
        }
        catch (InvalidOperationException)
        {
            // DbProjectionStatus entity is not configured in this context
            // This is expected if the user hasn't set up the projection daemon
            return false;
        }
    }
}

/// <summary>
/// Projection context implementation for EF Core providers.
/// </summary>
/// <param name="dbContext">The EF Core DbContext.</param>
/// <param name="services">Service provider for resolving dependencies.</param>
internal sealed class ProjectionContext(DbContext dbContext, IServiceProvider services) : IProjectionContext
{
    /// <summary>
    /// The EF Core DbContext associated with the projection.
    /// </summary>
    public DbContext DbContext { get; } = dbContext;

    /// <summary>
    /// The service provider for resolving dependencies.
    /// </summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>
    /// Provider-specific state for the projection.
    /// </summary>
    public object? ProviderState => DbContext;
}

