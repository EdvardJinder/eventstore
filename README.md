# EventStoreCore

## Install

```bash
dotnet add package EventStoreCore
dotnet add package EventStoreCore.Postgres
# or EventStoreCore.SqlServer
dotnet add package EventStoreCore.Hangfire
# or EventStoreCore.Quartz
# or EventStoreCore.TickerQ
# plus a TickerQ persistence package such as TickerQ.EntityFrameworkCore for durable jobs
```


## Quick start

```csharp
using EventStoreCore;
using EventStoreCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class MyEventStoreDbContext : DbContext
{
    public MyEventStoreDbContext(DbContextOptions<MyEventStoreDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEventStore();
    }
}

var services = new ServiceCollection();

services.AddDbContext<MyEventStoreDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddProjection<MyEventStoreDbContext, MyProjection, MySnapshot>(
        ProjectionMode.Inline,
        options => options.Handles<MyEvent>());
});
```

Projection and subscription daemons require an `IDistributedLockProvider`. Register any implementation (Redis, SQL Server, Postgres, etc.) in DI.

## Inline projection contract

Inline projections run inside the same `DbContext` scope and `SaveChanges` transaction that appends events. If an inline projection throws, the append is rolled back with it.

Inline projections should therefore be:

- Deterministic: the same event history should always produce the same snapshot state.
- Idempotent: reprocessing the same event should not produce duplicate side effects.
- Local: treat inline execution as part of persistence, not as a place for remote I/O.

### Pure vs Advanced

`Pure` inline projections only mutate their snapshot from event data. This is the safest default and the easiest mode to reason about.

`Advanced` inline projections may use the projection context and shared EF Core scope to update additional local state in the same transaction. Keep that work inside the database boundary and make it idempotent.

Avoid network calls, message publishing, HTTP requests, and other remote side effects in inline projections. Inline projections can be retried, rolled back, or skipped during rebuild flows, so external side effects belong in subscriptions or eventual projections instead.

Subscriptions and eventual projections are at-least-once. Consumers should use `EventId` as a stable deduplication key when replay or retry can redeliver an event.

## Durable scheduled work

Use scheduler-backed subscriptions for durable delayed work. Keep scheduling out of inline projections: delayed jobs are external side effects and belong in the at-least-once subscription pipeline.

### Shared contract

The scheduler integrations currently supported by EventStoreCore follow the same workflow contract:

- Scheduling runs through a regular subscription, so delivery is at-least-once.
- `ScheduleKey` is the stable business identity for one logical scheduled action.
- The same `EventId` replayed with the same `ScheduleKey` is treated as a no-op.
- A later event using the same `ScheduleKey` replaces the previously scheduled work.
- Cancel for a missing, already-fired, or already-replaced schedule is treated as a no-op.
- Scheduled job handlers are resolved from DI through `IScheduledJobHandler<TArgs>`.
- The scheduler integrations aim for effectively-once scheduling of the same event/key pair, not end-to-end exactly-once execution.

### Guidance

- Use a stable `ScheduleKey` derived from business identity, for example `payment-timeout:{orderId}`.
- Include the source `EventId` in scheduled args when deduplication or audit trails matter.
- Keep scheduled job handlers idempotent and re-check current stream state before emitting new events or commands.
- Let the scheduler own durability and delayed execution. EventStore owns event delivery, replay, and subscription retries.
- Expect at-least-once execution overall. Subscription retries, scheduler retries, and crash windows still require idempotent job behavior.

### Support matrix

| Provider | Status | Application-owned setup | Notes |
|---|---|---|---|
| Hangfire | Supported | Hangfire storage and server lifetime | EventStoreCore keeps `ScheduleKey -> job id` correlation internally. |
| Quartz | Supported | Quartz scheduler storage and hosted service lifetime | EventStoreCore maps `ScheduleKey` to deterministic `JobKey`/`TriggerKey`. |
| TickerQ | Supported | TickerQ host startup plus a persistence package for durability | `TickerQ` alone is in-memory; use `TickerQ.EntityFrameworkCore` or Redis-backed storage for durable jobs. |

### Hangfire

Hangfire uses provider-managed job ids internally and EventStoreCore.Hangfire keeps the `ScheduleKey` to job-id correlation for replay-safe replacement and cancellation.
Applications remain responsible for configuring Hangfire storage and server lifetime.

```csharp
using EventStoreCore;
using EventStoreCore.Hangfire;
using EventStoreCore.Postgres;
using EventStoreCore.Scheduling;
using Hangfire;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed record PaymentTimeoutArgs(Guid OrderId, Guid SourceEventId);

public sealed class OrderPlaced
{
    public Guid OrderId { get; init; }
}

public sealed class PaymentDeadlineChanged
{
    public Guid OrderId { get; init; }
}

public sealed class PaymentCaptured
{
    public Guid OrderId { get; init; }
}

public sealed class PaymentTimeoutHandler(MyEventStoreDbContext dbContext)
    : IScheduledJobHandler<PaymentTimeoutArgs>
{
    public async Task HandleAsync(PaymentTimeoutArgs args, CancellationToken ct)
    {
        var stream = await dbContext.Streams.FetchForWritingAsync<OrderState>(args.OrderId, ct);

        // Re-check current business state before acting. The scheduled job may fire
        // after newer events have already resolved the timeout.
        if (stream is null || stream.State.IsPaid)
        {
            return;
        }

        stream.Append([new PaymentExpired { OrderId = args.OrderId }]);

        await dbContext.SaveChangesAsync(ct);
    }
}

var services = new ServiceCollection();

services.AddDbContext<MyEventStoreDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddHangfire(config => config.UsePostgreSqlStorage(connectionString));
services.AddSingleton<IDistributedLockProvider>(
    _ => new PostgresDistributedSynchronizationProvider(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingHangfire();

        s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(15),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));

        s.Schedule<PaymentDeadlineChanged, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(30),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));

        s.Cancel<PaymentCaptured>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"));
    });
});
```

### Quartz

Quartz maps `ScheduleKey` to deterministic `JobKey` and `TriggerKey` values. Replay-safe replacement and cancellation are implemented through those Quartz identities rather than through an external registry.
Applications remain responsible for configuring Quartz storage and hosted service lifetime.

```csharp
using EventStoreCore;
using EventStoreCore.Postgres;
using EventStoreCore.Quartz;
using EventStoreCore.Scheduling;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

public sealed record PaymentTimeoutArgs(Guid OrderId, Guid SourceEventId);

services.AddDbContext<MyEventStoreDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddQuartz();
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
services.AddSingleton<IDistributedLockProvider>(
    _ => new PostgresDistributedSynchronizationProvider(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingQuartz();

        s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(15),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));

        s.Schedule<PaymentDeadlineChanged, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(30),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));

        s.Cancel<PaymentCaptured>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"));
    });
});
```

### TickerQ

TickerQ stores the `ScheduleKey` on `TimeTickerEntity.Description` and uses a single EventStore-owned TickerQ function to dispatch scheduled payloads back into `IScheduledJobHandler<TArgs>`.

TickerQ uses in-memory storage by default. For durable delayed work, applications must configure a persistence package such as `TickerQ.EntityFrameworkCore` or Redis-backed storage, in addition to TickerQ host startup behavior.

```csharp
using EventStoreCore;
using EventStoreCore.Postgres;
using EventStoreCore.Scheduling;
using EventStoreCore.TickerQ;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;

public sealed record PaymentTimeoutArgs(Guid OrderId, Guid SourceEventId);

services.AddDbContext<MyEventStoreDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddTickerQ(options =>
{
    options.AddOperationalStore(ef =>
    {
        ef.UseTickerQDbContext(db => db.UseNpgsql(connectionString));
    });
});
services.AddSingleton<IDistributedLockProvider>(
    _ => new PostgresDistributedSynchronizationProvider(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddSubscriptionDaemon<MyEventStoreDbContext>();

    builder.AddScheduler(s =>
    {
        s.UsingTickerQ();

        s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(15),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));

        s.Schedule<PaymentDeadlineChanged, PaymentTimeoutArgs>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
            delay: _ => TimeSpan.FromMinutes(30),
            args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));

        s.Cancel<PaymentCaptured>(
            key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"));
    });
});
```

## Tenant-scoped checkpoints

Streams and events include `TenantId`. By default, subscription and projection daemons use global checkpoints for backwards compatibility: one checkpoint row tracks progress across all tenants.

Use tenant-scoped checkpoints when a poison event, pause, retry, skip, or replay in one tenant should not affect other tenants:

```csharp
using EventStoreCore.Abstractions;

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();

    builder.AddSubscriptionDaemon<MyEventStoreDbContext>(
        _ => distributedLockProvider,
        options => options.CheckpointScope = CheckpointScope.Tenant);

    builder.AddProjectionDaemon<MyEventStoreDbContext>(
        _ => distributedLockProvider,
        options =>
        {
            options.CheckpointScope = CheckpointScope.Tenant;
            options.AutoRebuildOnVersionChange = false;
        });
});
```

Global checkpoint rows use `CheckpointScope.Global`. Tenant checkpoint rows use `CheckpointScope.Tenant` plus the event `TenantId`, including `Guid.Empty` for the default tenant. The admin endpoints accept an optional `tenantId` query parameter for tenant-scoped status and operations, for example `POST /subscriptions/{name}/retry?tenantId={tenantId}`.

Tenant-scoped checkpoints isolate daemon progress and failure state; they do not automatically make projection snapshots tenant-isolated. If multiple tenants can use the same stream id, include tenant id in the projection key or snapshot key.

Projection rebuild remains global because `IProjection<TSnapshot>.ClearAsync` has no tenant parameter. Tenant-scoped projection checkpoints therefore do not support automatic version-change rebuilds; disable `AutoRebuildOnVersionChange` when using tenant-scoped projection checkpoints and run global rebuilds intentionally.

### Migration steps

1. Add `CheckpointScope` and `TenantId` columns to `Subscriptions` and `ProjectionStatuses`.
2. Backfill existing rows with `CheckpointScope.Global` and `TenantId = Guid.Empty`.
3. Update the primary keys to `(SubscriptionAssemblyQualifiedName, CheckpointScope, TenantId)` and `(ProjectionName, CheckpointScope, TenantId)`.

## Event type names

EventStore now persists a logical event type name in `DbEvent.TypeName`. By default, it uses snake_case based on the CLR type name (for example, `UserCreated` becomes `user_created`).

- Register custom names when needed: `builder.AddEvent<UserCreated>("user_created_v2")`.
- If you do not register an event, the default snake_case name is used automatically on write.
- Event materialization throws `EventMaterializationException` if the event type cannot be resolved.

### Migration steps

1. Add a `TypeName` column (NOT NULL, default empty string) to the `Events` table.
2. Populate `TypeName` for existing rows using your preferred backfill process.
3. Optionally tighten constraints (remove the default or enforce non-empty values) once values are populated.

## Stream types

EventStore supports multiple streams with the same ID but different types, enabling scenarios like:
- Document upload/lifecycle stream and document analysis stream sharing the same document ID
- Order processing stream and order audit stream sharing the same order ID

Stream type is specified as the first parameter when calling `IEventStore` methods:

```csharp
// Create different stream types with the same ID
var docId = Guid.NewGuid();
eventStore.StartStream("document-lifecycle", docId, events: [new DocumentCreated()]);
eventStore.StartStream("document-analysis", docId, events: [new AnalysisStarted()]);

// Fetch specific stream types
var lifecycleStream = await eventStore.FetchForReadingAsync("document-lifecycle", docId);
var analysisStream = await eventStore.FetchForReadingAsync("document-analysis", docId);

// Default stream type (empty string)
eventStore.StartStream(docId, events: [new SomeEvent()]);
var stream = await eventStore.FetchForReadingAsync(docId);
```

**Default behavior**: Overloads without `streamType` default to an empty string `""`, maintaining backwards compatibility.

### Migration steps for existing databases

1. Add a `StreamType` column (NOT NULL, default empty string) to both the `Streams` and `Events` tables.
2. Update the primary key on `Streams` from `Id` to `(Id, StreamType)`.
3. Update the primary key on `Events` from `(StreamId, Version)` to `(StreamId, StreamType, Version)`.
4. Update the foreign key relationship between `Events` and `Streams` to include `StreamType`.
5. Update indexes to include `StreamType` where appropriate.

**Note**: Changing primary keys in existing databases requires careful migration planning. Consider the impact on your application and data before applying these changes.

## Optimistic concurrency

Use `AppendAsync` when callers need explicit expected-version semantics instead of fetch-and-append behavior.

```csharp
await eventStore.AppendAsync(
    streamId,
    ExpectedVersion.NoStream,
    [new AccountOpened()]);

await eventStore.AppendAsync(
    streamId,
    ExpectedVersion.Exact(3),
    [new FundsDeposited(100m)]);
```

Supported modes:

- `ExpectedVersion.Any`: append whether the stream exists or not.
- `ExpectedVersion.NoStream`: succeed only when the stream does not already exist.
- `ExpectedVersion.StreamExists`: succeed only when the stream already exists.
- `ExpectedVersion.Exact(version)`: succeed only when the stream exists at the supplied version.

When an expected-version check fails, or when two writers race and the database wins the tie-breaker, EventStore throws `EventStoreConcurrencyException`.

The final optimistic concurrency guard is enforced by the event table key over stream identity plus event version. This means concurrent writers to the same stream cannot both commit the same next version.

## Project guidelines

- Keep public APIs small, composable, and backwards compatible.
- Document all `public` types and members with XML docs.
- Favor explicit configuration over magic defaults; surface options via builders.
- Keep EF Core provider logic isolated to provider-specific projects.
- Projections and subscriptions should be deterministic and idempotent.
- Add tests for new behaviors using `EventStoreCore.Testing` helpers.

## Testing



Install the test helpers:

```bash
dotnet add package EventStoreCore.Testing
```

Behavior-style tests call your stream extension methods directly. `Given` seeds history, `When` appends new events, and `Then` asserts only the new events in order.

```csharp
using EventStoreCore.Testing;

public sealed class ItemTypeTests : StreamBehaviorTest<ItemTypeState>
{
    [Fact]
    public void create_emits_created()
    {
        When(s => s.Create(itemTypeId, mspId, clientId, "Widget", "desc"));

        Then(new ItemTypeCreated(itemTypeId, mspId, clientId, "Widget", "desc"));
    }
}
```
