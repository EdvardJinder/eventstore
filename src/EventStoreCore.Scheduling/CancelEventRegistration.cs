using EventStoreCore.Abstractions;

namespace EventStoreCore.Scheduling;

internal sealed class CancelEventRegistration<TEvent>(
    Func<IEvent<TEvent>, ScheduleKey> key) : IEventScheduleRegistration
    where TEvent : class
{
    public Type EventType => typeof(TEvent);

    public Task ApplyAsync(IEvent @event, ISchedulerExecutionAdapter adapter, CancellationToken ct)
    {
        var typedEvent = (IEvent<TEvent>)@event;
        return adapter.CancelAsync(key(typedEvent), ct);
    }
}
