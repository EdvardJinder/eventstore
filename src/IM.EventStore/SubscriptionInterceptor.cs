using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

internal sealed class SubscriptionInterceptor(
    IServiceScopeFactory serviceScopeFactory
    ) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not DbContext db)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var streams = db.ChangeTracker.Entries<DbStream>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        using var scope = serviceScopeFactory.CreateScope();
        // Get all subscriptions
        var subscriptions = scope.ServiceProvider.GetServices<ISubscription>();

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

            foreach (var subscription in subscriptions)
            {
                await subscription.HandleBatchAsync(events, cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

