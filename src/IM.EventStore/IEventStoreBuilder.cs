using IM.EventStore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public interface IEventStoreBuilder
{
    public IServiceCollection Services { get; }
    public object? Provider { get; }
    public void UseProvider(object provider);

    IEventStoreBuilder AddSubscription<TSubscription>()
        where TSubscription : ISubscription;


}

