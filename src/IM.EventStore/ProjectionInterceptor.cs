using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IM.EventStore;


internal sealed class ProjectionInterceptor<TProjection, TSnapshot>(ProjectionOptions options) : SaveChangesInterceptor
    where TProjection : IProjection<TSnapshot>, new()
    where TSnapshot : class, new()
{
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
                .Where(e => options.IsHandeled(e.EventType))
                .ToArray();

            if (events is not { Length: > 0 })
            {
                continue;
            }

            foreach (var @event in events.OrderBy(e => e.Version))
            {
                var keySelector = options.GetKeySelector(@event.EventType);

                var key = keySelector((IEvent<object>)@event);

                var snaphost = await db
                    .Set<TSnapshot>()
                    .FindAsync([key], cancellationToken);

                if (snaphost is null)
                {
                    snaphost = new TSnapshot();
                    await TProjection.Evolve(snaphost, @event, db, cancellationToken);
                    db.Add(snaphost);
                }
                else
                {
                    await TProjection.Evolve(snaphost, @event, db, cancellationToken);
                }
            }
        }
        return result;
    }
}

