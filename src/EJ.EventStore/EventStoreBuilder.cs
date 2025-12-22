using EJ.EventStore.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EJ.EventStore;

internal sealed class EventStoreBuilder(
    IServiceCollection services
    ) : IEventStoreBuilder
{
    public IServiceCollection Services => services;
    public object? Provider { get; private set; }
    public void UseProvider(object provider)
    {
        Provider = provider;
    }

    public IEventStoreBuilder AddSubscription<TSubscription>() where TSubscription : ISubscription
    {
        services.TryAddSingleton(typeof(TSubscription));
        services.TryAddSingleton(typeof(ISubscription), sp => sp.GetRequiredService<TSubscription>());
        return this;
    }
}
