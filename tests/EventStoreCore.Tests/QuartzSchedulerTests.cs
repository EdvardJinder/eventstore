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

    [Fact]
    public async Task should_not_reschedule_same_event_after_original_trigger_has_fired()
    {
        PaymentTimeoutHandler.Reset();

        var provider = BuildProvider(s =>
        {
            s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
                key: e => PaymentTimeoutKey(e.Data.OrderId),
                delay: _ => TimeSpan.FromMilliseconds(50),
                args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
        });

        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);
        await scheduler.Start(TestContext.Current.CancellationToken);

        var orderId = Guid.NewGuid();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var subscription = GetSubscription(provider);

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await WaitForAsync(() => PaymentTimeoutHandler.Executed.Count == 1, TimeSpan.FromSeconds(2));

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Single(PaymentTimeoutHandler.Executed);
        Assert.True(await scheduler.CheckExists(QuartzScheduleIdentity.GetJobKey(PaymentTimeoutKey(orderId)), TestContext.Current.CancellationToken));
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Condition was not met within the allotted timeout.");
    }
}
