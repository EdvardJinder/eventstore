using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

internal class InlineProjection<TSnapshot, TProjection, TDbContext> : SaveChangesInterceptor
    where TSnapshot : class, new()
    where TProjection : IInlineProjection<TSnapshot>
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public InlineProjection(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if(eventData.Context is not DbContext db)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var events = db.ChangeTracker.Entries<DbStream>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .SelectMany(e => e.Entity.Events)
            .GroupBy(e => e.StreamId)
            .ToList();

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var projection = scope.ServiceProvider.GetRequiredService<TProjection>();

        var dbSet = db.Set<TSnapshot>();


        foreach (var stream in events)
        {
            var snapshot = dbSet.Find(stream.Key);

            if(snapshot is null)
            {
                snapshot = new TSnapshot();
                dbSet.Add(snapshot);
            }

            foreach (var @event in stream)
            {
                var eventType = Type.GetType(@event.Type);
                var genericType = typeof(Event<>).MakeGenericType(eventType!);

                var evnt = (IEvent)Activator.CreateInstance(genericType, @event)!;

                projection.Evolve(snapshot, evnt);

            }
        }


        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}