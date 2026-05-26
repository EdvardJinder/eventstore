# EventStoreCore.Quartz

Quartz-backed durable scheduler integration for EventStoreCore.

## Install

```bash
dotnet add package EventStoreCore
dotnet add package EventStoreCore.Quartz
dotnet add package Quartz.Extensions.DependencyInjection
dotnet add package Quartz.Extensions.Hosting
```

## Usage

Configure Quartz in your application, then register the EventStoreCore integration:

```csharp
services.AddQuartz();
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

services.AddEventStore(builder =>
{
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingQuartz();

        s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(15),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
    });
});
```

## Contract

- Scheduling is replay-aware for the same `EventId` and `ScheduleKey`.
- `ScheduleKey` is mapped to deterministic Quartz identities for replacement and cancellation.
- Cancel for a missing or already-removed schedule is a no-op.
- Scheduled handlers are resolved from DI through `IScheduledJobHandler<TArgs>`.

This package does not provide end-to-end exactly-once execution. Job handlers must remain idempotent.
