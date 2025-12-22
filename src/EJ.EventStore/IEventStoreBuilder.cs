using EJ.EventStore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EJ.EventStore;

public interface IEventStoreBuilder
{
    public IServiceCollection Services { get; }
    public object? Provider { get; }
    public void UseProvider(object provider);

    IEventStoreBuilder AddSubscription<TSubscription>()
        where TSubscription : ISubscription;


}

