using EventStoreCore.Scheduling;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace EventStoreCore.Hangfire;

internal sealed class HangfireScheduleService(
    IBackgroundJobClient backgroundJobClient,
    HangfireScheduleRegistry registry) : ISchedulerExecutionAdapter
{
    public Task ScheduleAsync<TArgs>(
        ScheduleKey key,
        Guid sourceEventId,
        TimeSpan delay,
        TArgs args,
        CancellationToken ct)
        where TArgs : class
    {
        ct.ThrowIfCancellationRequested();

        var existing = registry.Get(key);
        if (existing is not null)
        {
            if (existing.SourceEventId == sourceEventId && registry.JobExists(existing.JobId))
            {
                return Task.CompletedTask;
            }

            backgroundJobClient.ChangeState(existing.JobId, new DeletedState());
        }

        var job = Job.FromExpression<HangfireScheduledJob<TArgs>>(runner => runner.ExecuteAsync(args, CancellationToken.None));
        var jobId = backgroundJobClient.Create(job, new ScheduledState(delay));

        registry.Save(key, jobId, sourceEventId);
        return Task.CompletedTask;
    }

    public Task CancelAsync(ScheduleKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var existing = registry.Get(key);
        if (existing is null)
        {
            return Task.CompletedTask;
        }

        backgroundJobClient.ChangeState(existing.JobId, new DeletedState());
        registry.Remove(key);
        return Task.CompletedTask;
    }
}
