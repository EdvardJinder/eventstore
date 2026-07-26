# EventStoreCore.Quartz

Quartz-backed scheduler integration for EventStoreCore subscriptions.

## Usage

Configure Quartz normally, then register a provider-native action:

```csharp
services.AddQuartz();
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingQuartz();

        s.On<OrderPlaced>().Quartz("payment-timeout", async (e, scheduler, sp, ct) =>
        {
            var jobKey = new JobKey($"payment-timeout:{e.Data.OrderId}", "payments");
            var triggerKey = new TriggerKey($"payment-timeout:{e.Data.OrderId}", "payments");

            if (await scheduler.CheckExists(jobKey, ct))
            {
                await scheduler.DeleteJob(jobKey, ct);
            }

            var job = JobBuilder.Create<PaymentTimeoutQuartzJob>()
                .WithIdentity(jobKey)
                .UsingJobData("order-id", e.Data.OrderId.ToString("D"))
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
});
```

## Contract

- The application owns its `DbContext`, EventStoreCore migrations, Quartz
  storage, and hosted-service lifetime. `ExistingDbContext<TDbContext>()`
  enables database-backed replay deduplication for scheduler actions.
- The action receives Quartz `IScheduler`.
- The `IServiceProvider` callback argument is scoped to the action invocation.
- EventStoreCore invokes the action at most once for the same registration, tenant id, and EventStore `EventId`.
- Quartz owns job persistence, trigger semantics, execution, cancellation, and replacement behavior.
- Job bodies must remain idempotent and re-check current stream state before acting.
