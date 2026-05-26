using EventStoreCore.Abstractions;
using EventStoreCore.Hangfire;
using EventStoreCore.Scheduling;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public class HangfireSchedulerTests : SchedulerContractTestsBase
{
    protected override ServiceProvider BuildProvider(Action<ISchedulerBuilder> configureScheduler)
    {
        var services = new ServiceCollection();

        var storage = new MemoryStorage();
        services.AddSingleton<JobStorage>(storage);
        services.AddSingleton<IBackgroundJobClient>(sp => new BackgroundJobClient(sp.GetRequiredService<JobStorage>()));
        services.AddTransient<IScheduledJobHandler<PaymentTimeoutArgs>, PaymentTimeoutHandler>();

        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingHangfire();
            configureScheduler(s);
        }));

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    protected override ISubscription GetSubscription(IServiceProvider provider)
    {
        return provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
    }

    protected override Task<string?> GetScheduledIdentityAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var registry = provider.GetRequiredService<HangfireScheduleRegistry>();
        return Task.FromResult(registry.Get(key)?.JobId);
    }

    protected override Task<Guid?> GetScheduledSourceEventIdAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var registry = provider.GetRequiredService<HangfireScheduleRegistry>();
        return Task.FromResult(registry.Get(key)?.SourceEventId as Guid?);
    }

    protected override Task<bool> ScheduleExistsAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var registry = provider.GetRequiredService<HangfireScheduleRegistry>();
        return Task.FromResult(registry.Get(key) is not null);
    }

    protected override Task InvokeScheduledJobAsync(
        IServiceProvider provider,
        PaymentTimeoutArgs args,
        CancellationToken ct)
    {
        var job = provider.GetRequiredService<HangfireScheduledJob<PaymentTimeoutArgs>>();
        return job.ExecuteAsync(args, ct);
    }
}
