using EventStoreCore.Scheduling;
using Quartz;
using System.Text.Json;

namespace EventStoreCore.Quartz;

internal sealed class QuartzScheduledJob<TArgs>(IScheduledJobHandler<TArgs> handler) : IJob
    where TArgs : class
{
    public async Task Execute(IJobExecutionContext context)
    {
        var payloadJson = context.MergedJobDataMap.GetString(QuartzScheduleService.PayloadJsonKey);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidOperationException("Quartz scheduled job payload is missing.");
        }

        var args = JsonSerializer.Deserialize<TArgs>(payloadJson)
            ?? throw new InvalidOperationException($"Quartz scheduled job payload for '{typeof(TArgs).FullName}' could not be deserialized.");

        await handler.HandleAsync(args, context.CancellationToken);
    }
}
