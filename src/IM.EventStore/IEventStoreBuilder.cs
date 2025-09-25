
namespace IM.EventStore;

public interface IEventStoreBuilder
{
    IEventStoreBuilder AddSubscription<TSubscription>(Action<ISubscriptionOptions>? configure = null)
        where TSubscription : ISubscription;

    IEventStoreBuilder AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
        where TProjection : IProjection<TSnapshot>
        where TSnapshot : class, new();

}
