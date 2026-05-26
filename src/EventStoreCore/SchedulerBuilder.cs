using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

internal sealed class SchedulerBuilder(IServiceCollection services) : ISchedulerBuilder
{
    public IServiceCollection Services => services;

    public ISchedulerBuilder Schedule<TEvent, TArgs>(
        Func<IEvent<TEvent>, ScheduleKey> key,
        Func<IEvent<TEvent>, TimeSpan> delay,
        Func<IEvent<TEvent>, TArgs> args)
        where TEvent : class
        where TArgs : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(args);

        services.AddOptions<SchedulerOptions>()
            .Configure(options =>
            {
                options.Registrations.Add(new ScheduleEventRegistration<TEvent, TArgs>(key, delay, args));
                options.RegisterPayloadType(typeof(TArgs));
            });

        return this;
    }

    public ISchedulerBuilder Cancel<TEvent>(Func<IEvent<TEvent>, ScheduleKey> key)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(key);

        services.AddOptions<SchedulerOptions>()
            .Configure(options => options.Registrations.Add(new CancelEventRegistration<TEvent>(key)));

        return this;
    }
}
