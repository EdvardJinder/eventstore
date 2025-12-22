using EJ.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EJ.EventStore.Persistence.EntityFrameworkCore;


public sealed class ProjectionInterceptor<TProjection, TSnapshot> : SaveChangesInterceptor
    where TProjection : IProjection<TSnapshot>, new()
    where TSnapshot : class, new()
{
    private readonly ProjectionOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public ProjectionInterceptor(ProjectionOptions options, IServiceProvider serviceProvider)
    {
        _options = options;
        _serviceProvider = serviceProvider;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not DbContext db)
        {
            return result;
        }

        var streams = db.ChangeTracker.Entries<DbStream>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        foreach (var stream in streams)
        {
            var events = db.ChangeTracker.Entries<DbEvent>()
                .Where(e => e.State is EntityState.Added && e.Entity.StreamId == stream.Entity.Id)
                .OrderBy(e => e.Entity.Version)
                .Select(e => e.Entity.ToEvent())
                .Where(e => _options.IsHandeled(e.EventType))
                .ToArray();

            if (events is not { Length: > 0 })
            {
                continue;
            }

            using var scope = _serviceProvider.CreateScope();
            var projectionContext = new ProjectionContext(db, scope.ServiceProvider);

            foreach (var @event in events.OrderBy(e => e.Version))
            {
                var keySelector = _options.GetKeySelector(@event.EventType);

                var key = keySelector((IEvent<object>)@event);

                var snaphost = await db
                    .Set<TSnapshot>()
                    .FindAsync([key], cancellationToken);

                if (snaphost is null)
                {
                    snaphost = new TSnapshot();
                    await TProjection.Evolve(snaphost, @event, projectionContext, cancellationToken);
                    db.Add(snaphost);
                }
                else
                {
                    await TProjection.Evolve(snaphost, @event, projectionContext, cancellationToken);
                }
            }
        }
        return result;
    }
}

internal sealed class ProjectionContext(DbContext dbContext, IServiceProvider services) : IProjectionContext
{
    public DbContext DbContext { get; } = dbContext;
    public IServiceProvider Services { get; } = services;
    public object? ProviderState => DbContext;
}
