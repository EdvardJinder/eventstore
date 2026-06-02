using EventStoreCore.Abstractions;
using EventStoreCore.Quartz;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl.Matchers;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public class QuartzSchedulerTests
{
    [Fact]
    public async Task should_invoke_quartz_action_once_for_replayed_event()
    {
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Quartz(static async (e, scheduler, _, ct) =>
            {
                var jobKey = new JobKey($"payment-timeout:{e.Data.OrderId}", "payments");
                var triggerKey = new TriggerKey($"payment-timeout:{e.Data.OrderId}", "payments");
                var job = JobBuilder.Create<QuartzProbeJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData("source-event-id", e.Id.ToString("D"))
                    .Build();
                var trigger = TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .ForJob(job)
                    .StartAt(DateBuilder.FutureDate(15, IntervalUnit.Minute))
                    .Build();

                await scheduler.ScheduleJob(job, trigger, ct);
            });
        });
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);
        var subscription = provider.GetServices<ISubscription>().OfType<QuartzSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Single(await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), TestContext.Current.CancellationToken));
        Assert.Equal(
            placed.Id.ToString("D"),
            (await scheduler.GetJobDetail(new JobKey($"payment-timeout:{placed.Data.OrderId}", "payments"), TestContext.Current.CancellationToken))!
                .JobDataMap.GetString("source-event-id"));
    }

    [Fact]
    public async Task should_run_each_registration_for_same_event_and_args()
    {
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Quartz("payment-reminder", static async (e, scheduler, _, ct) =>
            {
                await scheduler.ScheduleJob(
                    JobBuilder.Create<QuartzProbeJob>().WithIdentity($"reminder:{e.Data.OrderId}", "payments").Build(),
                    TriggerBuilder.Create().WithIdentity($"reminder:{e.Data.OrderId}", "payments").StartNow().Build(),
                    ct);
            });
            s.On<OrderPlaced>().Quartz("payment-escalation", static async (e, scheduler, _, ct) =>
            {
                await scheduler.ScheduleJob(
                    JobBuilder.Create<QuartzProbeJob>().WithIdentity($"escalation:{e.Data.OrderId}", "payments").Build(),
                    TriggerBuilder.Create().WithIdentity($"escalation:{e.Data.OrderId}", "payments").StartNow().Build(),
                    ct);
            });
        });
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);
        var subscription = provider.GetServices<ISubscription>().OfType<QuartzSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Equal(2, (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task should_allow_action_to_use_quartz_replace_semantics()
    {
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Quartz(static async (e, scheduler, _, ct) =>
            {
                var jobKey = new JobKey($"payment-timeout:{e.Data.OrderId}", "payments");
                var triggerKey = new TriggerKey($"payment-timeout:{e.Data.OrderId}", "payments");
                if (await scheduler.CheckExists(jobKey, ct))
                {
                    await scheduler.DeleteJob(jobKey, ct);
                }

                var job = JobBuilder.Create<QuartzProbeJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData("source-event-id", e.Id.ToString("D"))
                    .Build();
                var trigger = TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .ForJob(job)
                    .StartAt(DateBuilder.FutureDate(30, IntervalUnit.Minute))
                    .Build();

                await scheduler.ScheduleJob(job, trigger, ct);
            });
        });
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);
        var orderId = Guid.NewGuid();
        var first = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var second = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var subscription = provider.GetServices<ISubscription>().OfType<QuartzSubscription>().Single();

        await subscription.Handle(first, TestContext.Current.CancellationToken);
        await subscription.Handle(second, TestContext.Current.CancellationToken);

        var job = await scheduler.GetJobDetail(new JobKey($"payment-timeout:{orderId}", "payments"), TestContext.Current.CancellationToken);
        Assert.NotNull(job);
        Assert.Equal(second.Id.ToString("D"), job.JobDataMap.GetString("source-event-id"));
        Assert.Single(await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task should_execute_scheduled_quartz_job()
    {
        QuartzProbeJob.Reset();
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Quartz(static async (e, scheduler, _, ct) =>
            {
                var job = JobBuilder.Create<QuartzProbeJob>()
                    .WithIdentity($"execute:{e.Data.OrderId}", "payments")
                    .UsingJobData("order-id", e.Data.OrderId.ToString("D"))
                    .Build();
                var trigger = TriggerBuilder.Create()
                    .WithIdentity($"execute:{e.Data.OrderId}", "payments")
                    .ForJob(job)
                    .StartNow()
                    .Build();

                await scheduler.ScheduleJob(job, trigger, ct);
            });
        });
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);
        var subscription = provider.GetServices<ISubscription>().OfType<QuartzSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await scheduler.Start(TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        await SchedulerTestWait.WaitForAsync(() => QuartzProbeJob.Executed.Contains(placed.Data.OrderId), TimeSpan.FromSeconds(5));
        await scheduler.Shutdown(waitForJobsToComplete: true, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildProvider(Action<EventStoreCore.Scheduling.ISchedulerBuilder> configureScheduler)
    {
        var services = new ServiceCollection();
        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();
        });
        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingQuartz();
            configureScheduler(s);
        }));
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}

public sealed class QuartzProbeJob : IJob
{
    private static readonly object Gate = new();

    public static List<Guid> Executed { get; } = [];

    public Task Execute(IJobExecutionContext context)
    {
        if (Guid.TryParse(context.MergedJobDataMap.GetString("order-id"), out var orderId))
        {
            lock (Gate)
            {
                Executed.Add(orderId);
            }
        }

        return Task.CompletedTask;
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Executed.Clear();
        }
    }
}
