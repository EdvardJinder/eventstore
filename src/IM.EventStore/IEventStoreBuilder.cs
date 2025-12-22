using IM.EventStore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public interface IEventStoreBuilder
{
    public IServiceCollection Services { get; }

    IEventStoreBuilder AddSubscription<TSubscription>()
        where TSubscription : ISubscription;


}

