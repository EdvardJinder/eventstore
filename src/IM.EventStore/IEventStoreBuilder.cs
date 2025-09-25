namespace IM.EventStore;

public interface IEventStoreBuilder
{
    IEventStoreBuilder AddSubscription<TSubscription>()
        where TSubscription : ISubscription;

    IEventStoreBuilder AddProjection<TProjection, TSnapshot>()
        where TProjection : IProjection<TSnapshot>
        where TSnapshot : class, new();

}

