using EventStoreCore.Scheduling;
using Quartz;
using System.Text.Json;

namespace EventStoreCore.Quartz;

internal sealed class QuartzScheduleService(ISchedulerFactory schedulerFactory)
    : ISchedulerExecutionAdapter
{
    internal const string PayloadJsonKey = "eventstore.payload-json";
    internal const string SourceEventIdKey = "eventstore.source-event-id";

    public async Task ScheduleAsync<TArgs>(
        ScheduleKey key,
        Guid sourceEventId,
        TimeSpan delay,
        TArgs args,
        CancellationToken ct)
        where TArgs : class
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var jobKey = QuartzScheduleIdentity.GetJobKey(key);
        var triggerKey = QuartzScheduleIdentity.GetTriggerKey(key);
        var existingJob = await scheduler.GetJobDetail(jobKey, ct);

        if (existingJob is not null)
        {
            var existingSourceEventId = existingJob.JobDataMap.GetString(SourceEventIdKey);
            if (string.Equals(existingSourceEventId, sourceEventId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await scheduler.DeleteJob(jobKey, ct);
        }

        var job = JobBuilder.Create<QuartzScheduledJob<TArgs>>()
            .WithIdentity(jobKey)
            .UsingJobData(SourceEventIdKey, sourceEventId.ToString("D"))
            .UsingJobData(PayloadJsonKey, JsonSerializer.Serialize(args))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.Add(delay))
            .Build();

        await scheduler.ScheduleJob(job, trigger, ct);
    }

    public async Task CancelAsync(ScheduleKey key, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var jobKey = QuartzScheduleIdentity.GetJobKey(key);

        if (!await scheduler.CheckExists(jobKey, ct))
        {
            return;
        }

        await scheduler.DeleteJob(jobKey, ct);
    }
}
