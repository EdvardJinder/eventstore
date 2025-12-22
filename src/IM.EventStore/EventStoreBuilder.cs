using IM.EventStore.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IM.EventStore;

internal sealed class EventStoreBuilder(
    IServiceCollection services
    ) : IEventStoreBuilder
{
    public IServiceCollection Services => services;

    public IEventStoreBuilder AddSubscription<TSubscription>() where TSubscription : ISubscription
    {
        services.TryAddSingleton(typeof(TSubscription));
        services.TryAddSingleton(typeof(ISubscription), sp => sp.GetRequiredService<TSubscription>());
        return this;
    }
}
