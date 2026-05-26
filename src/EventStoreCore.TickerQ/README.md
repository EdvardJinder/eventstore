# EventStoreCore.TickerQ

TickerQ-backed durable scheduler integration for EventStoreCore.

## Install

```bash
dotnet add package EventStoreCore
dotnet add package EventStoreCore.TickerQ
dotnet add package TickerQ
dotnet add package TickerQ.EntityFrameworkCore
```

## Usage

Configure TickerQ with a persistence provider in your application, then register the EventStoreCore integration:

```csharp
services.AddTickerQ(options =>
{
    options.DisableBackgroundServices();
    options.AddOperationalStore(ef =>
    {
        ef.UseTickerQDbContext(db => db.UseNpgsql(connectionString));
    });
});

services.AddEventStore(builder =>
{
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingTickerQ();

        s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(15),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
    });
});
```

## Contract

- Scheduling is replay-aware for the same `EventId` and `ScheduleKey`.
- `ScheduleKey` is stored on the TickerQ `TimeTickerEntity.Description` field for replacement and cancellation.
- Cancel for a missing or already-removed schedule is a no-op.
- Scheduled handlers are resolved from DI through `IScheduledJobHandler<TArgs>`.
- Durable scheduling requires a TickerQ persistence package. The core `TickerQ` package alone uses in-memory storage.

This package does not provide end-to-end exactly-once execution. Job handlers must remain idempotent.
