using EventStoreCore.Abstractions;

namespace EventStoreCore.Scheduling;

internal sealed class ScheduleEventRegistration<TEvent, TArgs>(
    Func<IEvent<TEvent>, ScheduleKey> key,
    Func<IEvent<TEvent>, TimeSpan> delay,
    Func<IEvent<TEvent>, TArgs> args) : IEventScheduleRegistration
    where TEvent : class
    where TArgs : class
{
    public Type EventType => typeof(TEvent);

    public Task ApplyAsync(IEvent @event, ISchedulerExecutionAdapter adapter, CancellationToken ct)
    {
        var typedEvent = (IEvent<TEvent>)@event;
        return adapter.ScheduleAsync(key(typedEvent), typedEvent.Id, delay(typedEvent), args(typedEvent), ct);
    }
}
