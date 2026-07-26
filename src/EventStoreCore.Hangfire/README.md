# EventStoreCore.Hangfire

Hangfire-backed scheduler integration for EventStoreCore subscriptions.

## Usage

Configure Hangfire normally, then register a provider-native action:

```csharp
services.AddHangfire(config => config.UsePostgreSqlStorage(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingHangfire();

        s.On<OrderPlaced>().Hangfire("payment-timeout", (e, client, sp, ct) =>
        {
            client.Schedule<PaymentTimeoutJob>(
                job => job.ExecuteAsync(e.Data.OrderId, e.Id, CancellationToken.None),
                TimeSpan.FromMinutes(15));

            return ValueTask.CompletedTask;
        });
    });
});
```

## Contract

- The application owns its `DbContext`, EventStoreCore migrations, Hangfire
  storage, and Hangfire server lifetime. `ExistingDbContext<TDbContext>()`
  enables database-backed replay deduplication for scheduler actions.
- The action receives Hangfire `IBackgroundJobClient`.
- The `IServiceProvider` callback argument is scoped to the action invocation.
- EventStoreCore invokes the action at most once for the same registration, tenant id, and EventStore `EventId`.
- Hangfire owns job persistence, execution, retries, cancellation, and replacement behavior.
- Job bodies must remain idempotent and re-check current stream state before acting.
