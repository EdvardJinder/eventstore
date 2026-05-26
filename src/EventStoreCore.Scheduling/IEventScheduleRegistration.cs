using EventStoreCore.Abstractions;

namespace EventStoreCore.Scheduling;

internal interface IEventScheduleRegistration
{
    Type EventType { get; }

    Task ApplyAsync(IEvent @event, ISchedulerExecutionAdapter adapter, CancellationToken ct);
}
