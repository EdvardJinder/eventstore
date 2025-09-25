//using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore.Diagnostics;
//using Microsoft.Extensions.DependencyInjection;

//namespace IM.EventStore;

//internal class SubscriptionWrapper<TSubscription> : SaveChangesInterceptor
//    where TSubscription : ISubscription
//{
//    private readonly IServiceScopeFactory _serviceScopeFactory;
//    public SubscriptionWrapper(IServiceScopeFactory serviceScopeFactory)
//    {
//        _serviceScopeFactory = serviceScopeFactory;
//    }

//    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
//    {
//        if(eventData.Context is not DbContext dbContext)
//            return await base.SavedChangesAsync(eventData, result, cancellationToken);

//        var streams = dbContext.ChangeTracker
//            .Entries<DbStream>()
//            .Where(e => e.State is EntityState.Added or EntityState.Modified)
//            .Select(e => e.Entity)
//            .ToList();

//        if (!streams.Any())
//            return await base.SavedChangesAsync(eventData, result, cancellationToken);

//        await using var scope = _serviceScopeFactory.CreateAsyncScope();
//        var subscription = scope.ServiceProvider.GetRequiredService<TSubscription>();

//        foreach (var stream in streams)
//        {
//            var addedEvents = dbContext.ChangeTracker
//                .Entries<DbEvent>()
//                .Where(e => e.State == EntityState.Added && e.Entity.StreamId == stream.Id)
//                .Select(e => e.Entity)
//                .OrderBy(e => e.Version)
//                .ToList();

//            foreach (var @event in addedEvents)
//            {
//                var eventInstance = @event.ToEvent();
//                await subscription.OnEventAsync(eventInstance, cancellationToken);
//            }
//        }

//        return await base.SavedChangesAsync(eventData, result, cancellationToken);
//    }
//}

