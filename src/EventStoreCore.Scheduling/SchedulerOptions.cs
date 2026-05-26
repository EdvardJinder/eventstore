namespace EventStoreCore.Scheduling;

internal sealed class SchedulerOptions
{
    public List<IEventScheduleRegistration> Registrations { get; } = [];
}
