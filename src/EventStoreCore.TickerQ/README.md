# EventStoreCore.TickerQ

TickerQ-backed scheduler integration for EventStoreCore subscriptions.

## Usage

Configure TickerQ normally. Use a persistence package such as `TickerQ.EntityFrameworkCore` when scheduled work must survive process restarts.

```csharp
services.AddTickerQ(options =>
{
    options.AddOperationalStore(ef =>
    {
        ef.UseTickerQDbContext(db => db.UseNpgsql(connectionString));
    });
});

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingTickerQ();

        s.On<OrderPlaced>().TickerQ("payment-timeout", async (e, manager, sp, ct) =>
        {
            await manager.AddAsync(new TimeTickerEntity
            {
                Function = "PaymentTimeout",
                Description = $"payment-timeout:{e.Data.OrderId}",
                ExecutionTime = DateTime.UtcNow.AddMinutes(15)
            }, ct);
        });
    });
});
```

## Contract

- The application owns its `DbContext`, EventStoreCore migrations, TickerQ
  storage, and host startup. `ExistingDbContext<TDbContext>()` enables
  database-backed replay deduplication for scheduler actions.
- The action receives TickerQ `ITimeTickerManager<TimeTickerEntity>`.
- The `IServiceProvider` callback argument is scoped to the action invocation.
- EventStoreCore invokes the action at most once for the same registration, tenant id, and EventStore `EventId`.
- TickerQ owns ticker persistence, execution, cancellation, and replacement behavior.
- Durable scheduling requires a TickerQ persistence package. The core `TickerQ` package alone uses in-memory storage.
- Job bodies must remain idempotent and re-check current stream state before acting.
