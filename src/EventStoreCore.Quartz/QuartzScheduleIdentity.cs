using EventStoreCore.Scheduling;
using Quartz;

namespace EventStoreCore.Quartz;

internal static class QuartzScheduleIdentity
{
    private const string JobGroup = "eventstore.scheduler.jobs";
    private const string TriggerGroup = "eventstore.scheduler.triggers";

    public static JobKey GetJobKey(ScheduleKey key) => new(key.Value, JobGroup);

    public static TriggerKey GetTriggerKey(ScheduleKey key) => new(key.Value, TriggerGroup);
}
