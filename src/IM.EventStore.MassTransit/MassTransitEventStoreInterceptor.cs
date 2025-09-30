using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IM.EventStore.MassTransit;

internal class MassTransitEventStoreInterceptor(
    IBus bus
    ) : SaveChangesInterceptor
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
                .ToArray();

            if (events is not { Length: > 0 })
            {
                continue;
            }

            foreach (var @event in events.OrderBy(e => e.Version))
            {
                var eventType = typeof(EventContext<>).MakeGenericType(@event.EventType);
                var eventContext = Activator.CreateInstance(eventType)!;

                eventType.GetProperty(nameof(EventContext<object>.Data))?.SetValue(eventContext, @event.Data);
                eventType.GetProperty(nameof(EventContext<object>.EventId))?.SetValue(eventContext, @event.Id);
                eventType.GetProperty(nameof(EventContext<object>.StreamId))?.SetValue(eventContext, @event.StreamId);
                eventType.GetProperty(nameof(EventContext<object>.Version))?.SetValue(eventContext, @event.Version);
                eventType.GetProperty(nameof(EventContext<object>.Timestamp))?.SetValue(eventContext, @event.Timestamp);
                eventType.GetProperty(nameof(EventContext<object>.TenantId))?.SetValue(eventContext, @event.TenantId);

                await bus.Publish(eventContext, cancellationToken);
            }
        }
        return result;
    }

}
