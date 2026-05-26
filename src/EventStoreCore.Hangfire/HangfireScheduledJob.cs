using EventStoreCore.Scheduling;

namespace EventStoreCore.Hangfire;

internal sealed class HangfireScheduledJob<TArgs>(IScheduledJobHandler<TArgs> handler)
    where TArgs : class
{
    public Task ExecuteAsync(TArgs args, CancellationToken ct)
    {
        return handler.HandleAsync(args, ct);
    }
}
