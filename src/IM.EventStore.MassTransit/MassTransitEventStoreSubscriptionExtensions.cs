
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.MassTransit;

public static class MassTransitEventStoreSubscriptionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMassTransitEventStoreSubscription<TDbContext>(Action<IConfigureMassTransitEventStoreSubscriptionOptions> configure)
            where TDbContext : DbContext
        {

            services.AddOptions<MassTransitEventStoreSubscriptionOptions>().Configure(configure);
            services.AddSubscription<TDbContext, MassTransitEventStoreSubscription>(opts =>
            {
                opts.HandlesAllEvents();
                opts.SubscribeFromPresent();
            });

            return services;
        }
    }
}

public interface IConfigureMassTransitEventStoreSubscriptionOptions
{
    void Handle<TInEvent, TOutEvent>(Func<IEvent<TInEvent>, TOutEvent> transform)
        where TInEvent : class
        where TOutEvent : class;
}

internal class MassTransitEventStoreSubscriptionOptions : IConfigureMassTransitEventStoreSubscriptionOptions
{
    public List<(Type InEvent, Type OutEvent, Func<IEvent<object>, object> Transform)> Handlers { get; } = new();

    public void Handle<TInEvent, TOutEvent>(Func<IEvent<TInEvent>, TOutEvent> transform)
        where TInEvent : class
        where TOutEvent : class
    {
        Handlers.Add((typeof(TInEvent), typeof(TOutEvent), e => transform((IEvent<TInEvent>)e)!));
    }
}