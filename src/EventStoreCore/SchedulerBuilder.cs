using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

internal sealed class SchedulerBuilder(IServiceCollection services) : ISchedulerBuilder
{
    public IServiceCollection Services => services;

    public IEventSchedulerBuilder<TEvent> On<TEvent>()
        where TEvent : class
    {
        return new EventSchedulerBuilder<TEvent>(services);
    }
}

internal sealed class EventSchedulerBuilder<TEvent>(IServiceCollection services) : IEventSchedulerBuilder<TEvent>
    where TEvent : class
{
    public IServiceCollection Services => services;
}
