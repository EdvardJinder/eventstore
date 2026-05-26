using EventStoreCore.Abstractions;
using EventStoreCore.Quartz;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public class QuartzSchedulerTests : SchedulerContractTestsBase
{
    protected override ServiceProvider BuildProvider(Action<ISchedulerBuilder> configureScheduler)
    {
        var services = new ServiceCollection();
        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();
        });
        services.AddTransient<IScheduledJobHandler<PaymentTimeoutArgs>, PaymentTimeoutHandler>();
        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingQuartz();
            configureScheduler(s);
        }));
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    protected override ISubscription GetSubscription(IServiceProvider provider)
    {
        return provider.GetServices<ISubscription>().OfType<QuartzSubscription>().Single();
    }

    protected override async Task<string?> GetScheduledIdentityAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var jobKey = QuartzScheduleIdentity.GetJobKey(key);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(ct);
        var job = await scheduler.GetJobDetail(jobKey, ct);

        return job?.Key.ToString();
    }

    protected override async Task<Guid?> GetScheduledSourceEventIdAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var jobKey = QuartzScheduleIdentity.GetJobKey(key);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(ct);
        var job = await scheduler.GetJobDetail(jobKey, ct);
        var sourceEventId = job?.JobDataMap.GetString(QuartzScheduleService.SourceEventIdKey);

        return Guid.TryParse(sourceEventId, out var parsed) ? parsed : null;
    }

    protected override async Task<bool> ScheduleExistsAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(ct);
        return await scheduler.CheckExists(QuartzScheduleIdentity.GetJobKey(key), ct);
    }

    protected override async Task InvokeScheduledJobAsync(
        IServiceProvider provider,
        PaymentTimeoutArgs args,
        CancellationToken ct)
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap
        {
            [QuartzScheduleService.PayloadJsonKey] = System.Text.Json.JsonSerializer.Serialize(args)
        });
        context.CancellationToken.Returns(ct);

        var job = provider.GetRequiredService<QuartzScheduledJob<PaymentTimeoutArgs>>();
        await job.Execute(context);
    }
}
