# EventStoreCore.Hangfire

Hangfire-backed durable scheduler integration for EventStoreCore.

## Install

```bash
dotnet add package EventStoreCore
dotnet add package EventStoreCore.Hangfire
dotnet add package Hangfire.Core
```

## Usage

Configure Hangfire in your application, then register the EventStoreCore integration:

```csharp
services.AddHangfire(config => config.UsePostgreSqlStorage(connectionString));

services.AddEventStore(builder =>
{
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingHangfire();

        s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(15),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
    });
});
```

## Contract

- Scheduling is replay-aware for the same `EventId` and `ScheduleKey`.
- A later event using the same `ScheduleKey` replaces the existing Hangfire job.
- Cancel for a missing or already-removed job is a no-op.
- Scheduled handlers are resolved from DI through `IScheduledJobHandler<TArgs>`.

This package does not provide end-to-end exactly-once execution. Job handlers must remain idempotent.
