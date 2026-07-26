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

## EF entity outbox

Entity outbox capture lets ordinary EF entities emit domain or integration events without making those entities event-sourced. The entity change and its outbox rows are persisted by the same `SaveChanges` transaction.

Configure only the outbox model when the context does not host EventStoreCore streams:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEntityOutbox();
    }
}
```

When the same context also hosts EventStoreCore streams, call both `UseEventStore()` and `UseEntityOutbox()`. Outbox tables remain opt-in so existing event-store applications do not acquire a schema migration merely by upgrading.

Register capture rules independently of `AddEventStore`:

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddEntityOutbox<AppDbContext>(outbox =>
{
    outbox.AddEvent<OrderCreated>("order_created");

    outbox.For<Order>()
        .TenantId(order => order.TenantId)
        .On(change => change
            .Added(entry => new OrderCreated(entry.Entity.Id))
            .Modified(entry => entry.IsModified(order => order.Status)
                ? new OrderStatusChanged(
                    entry.Entity.Id,
                    entry.Original(order => order.Status),
                    entry.Current(order => order.Status))
                : null)
            .Deleted(entry =>
            [
                new OrderDeleted(entry.Entity.Id),
                new OrderAuditRecorded(entry.Entity.Id, "deleted")
            ]));
});
```

`Added`, `Modified`, and `Deleted` each have single-event and collection overloads. Return `null` from the single-event overload or `[]` from the collection overload when the change should not emit an event. Factories run synchronously before EF sends database commands, and exceptions abort `SaveChanges`.

Outbox payloads must use values that are stable before `SaveChanges`. Client-generated identifiers work naturally. Database-generated keys and other store-generated values are not available to the atomic capture callback.

Read explicitly through `IOutboxReader`, or register independently checkpointed at-least-once subscriptions and the optional daemon:

```csharp
services.AddOutboxSubscription<PublishOrderEvents>(
    options => options.Name = "publish-order-events");
services.AddEntityOutboxDaemon<AppDbContext>();

public sealed class PublishOrderEvents : IOutboxSubscription
{
    public Task Handle(IOutboxEvent @event, CancellationToken ct)
    {
        // Publish idempotently; use event.Id as the deduplication key.
        return Task.CompletedTask;
    }
}
```

Transport packages provide adapters over the same outbox subscription pipeline. For example:

```csharp
services.AddMassTransitOutboxSubscription(options =>
{
    options.AddEvent<OrderCreated, OrderCreatedMessage>(e =>
        new OrderCreatedMessage(e.Data.OrderId, e.Id));
});

services.AddEventGridOutboxSubscription(options =>
{
    options.MapEvent<OrderCreated>(
        "com.example.order.created",
        "urn:example:orders",
        e => e.SourceEntityKey);
});
```

The CloudEvents package also exposes `AddCloudEventOutboxSubscription<TPublisher>` for custom
`ICloudEventSubscription` publishers. Adapter mappings receive `IOutboxEvent<T>`, including the
stable outbox ID, tenant, sequence, source-entity metadata, and change kind.

Strongly typed subscriptions and filters use the registration options:

```csharp
services.AddOutboxSubscription<PublishOrderEvents, OrderCreated>(options =>
{
    options.Name = "publish-order-events";
    options.IncludeTenant(tenantId);
    options.IncludeSourceEntity<Order>();
    options.IncludeChangeKind(EntityChangeKind.Added);
    options.IncludeLogicalEventType("order_created");
});

public sealed class PublishOrderEvents : IOutboxSubscription<OrderCreated>
{
    public Task Handle(IOutboxEvent<OrderCreated> @event, CancellationToken ct)
    {
        // The envelope retains its outbox ID, sequence, tenant, entity key, and change kind.
        return Task.CompletedTask;
    }
}
```

Multiple values in one filter category are ORed and categories are ANDed.
Filtered events still advance the subscription checkpoint. Handlers are resolved
from a dependency-injection scope for each processed batch, so they may depend on
scoped services.

Unmaterializable events fail and enter the retry flow by default. Set
`UnknownEventPolicy` to `Skip` to advance, `Quarantine` to dead-letter
immediately, or call `HandleUnknown` to inspect the raw persisted event before
advancing.

Each outbox subscription has its own checkpoint, retry state, and distributed lock. One failed destination does not block another. Successfully consumed rows are retained for audit and replay; `IOutboxReader.CleanupAsync` deletes only through the slowest persisted subscription checkpoint. Stream subscriptions and entity-outbox subscriptions are separate logs and have no combined ordering.

Cleanup returns without deleting rows while any registered subscription has not
created its first checkpoint, preventing a newly added destination from losing
events before its daemon initializes.

Use an explicit stable name for long-lived subscriptions. The name is used by
checkpoints, distributed locks, logs, management APIs, and admin endpoints.
Without one, the assembly-qualified subscription type name is used for
compatibility, so renaming or moving the type creates a new checkpoint.

Resolve `IOutboxSubscriptionManager` to inspect and recover subscriptions:

```csharp
var status = await manager.GetStatusAsync("publish-order-events", ct);
var failed = await manager.GetFailedEventAsync("publish-order-events", ct);

await manager.RetryFailedEventAsync("publish-order-events", ct);
await manager.SkipFailedEventAsync("publish-order-events", ct);
await manager.PauseAsync("publish-order-events", ct);
await manager.ResumeAsync("publish-order-events", ct);
await manager.ReplayAsync("publish-order-events", startSequence: 42, ct: ct);
```

All operations have tenant-scoped overloads. The admin endpoint package exposes
the same operations under `/outbox-subscriptions`. To adopt a stable name for an
existing unnamed subscription without replaying it, stop every daemon instance,
update `OutboxSubscriptions.SubscriptionAssemblyQualifiedName`, deploy the
explicit name, and then restart the daemon.

The dispatcher records activities and metrics under `EventStoreCore.Daemons`
with daemon kind `outbox-subscription`, publishes `IDaemonFaultObserver`
notifications, and participates in `DaemonHealthMonitor`. It does not keep a
database transaction open while invoking a handler. Checkpoints are persisted
after successful delivery, so a crash between delivery and checkpointing can
redeliver the event and handlers must remain idempotent.

Add migrations for the `OutboxMessages` and `OutboxSubscriptions` tables after enabling the feature.

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

EventStoreCore integrates with schedulers at the subscription boundary. It does not try to normalize Hangfire, Quartz, and TickerQ into a lowest-common-denominator trigger model.

- Scheduling actions run through regular subscriptions, so event delivery is at-least-once.
- Each configured provider action is invoked at most once for the same provider, registration name, tenant id, and EventStore `EventId`.
- The action receives the provider-native scheduler object plus a scoped service provider, and owns scheduling, cancellation, replacement, trigger configuration, and job payload conventions.
- When `ExistingDbContext<TDbContext>()` is configured, EventStoreCore persists `SchedulerEventApplications` rows to make the once-per-event gate database-backed.
- If a process dies after claiming an event but before completing the scheduler action, stale incomplete claims can be recovered after the internal recovery timeout.
- This is not end-to-end exactly-once execution. Scheduler jobs and downstream handlers must remain idempotent and re-check current stream state.

### Guidance

- Give long-lived actions an explicit stable registration name, for example `payment-timeout`.
- Unnamed actions use a type-derived registration name and are best treated as development convenience; explicit names are safer for production replay identity.
- Use provider-native stable identities for logical schedules, such as Hangfire job ids you store yourself, Quartz `JobKey`/`TriggerKey`, or TickerQ entity fields.
- Use the provider's native replace/cancel APIs inside the action when a later event should update earlier scheduled work.
- Include the source `EventId` in provider job payloads when audit trails or downstream dedupe matter.
- Keep the actual job body idempotent and re-check current stream state before emitting new events or commands.

### Support matrix

| Provider | Status | Application-owned setup | Notes |
|---|---|---|---|
| Hangfire | Supported | Hangfire storage and server lifetime | Actions receive `IBackgroundJobClient`. |
| Quartz | Supported | Quartz scheduler storage and hosted service lifetime | Actions receive Quartz `IScheduler`. |
| TickerQ | Supported | TickerQ host startup plus a persistence package for durability | Actions receive `ITimeTickerManager<TimeTickerEntity>`. |

### Hangfire

Applications remain responsible for configuring Hangfire storage and server lifetime.

```csharp
using EventStoreCore;
using EventStoreCore.Hangfire;
using EventStoreCore.Postgres;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
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

public sealed class PaymentTimeoutJob(MyEventStoreDbContext dbContext)
{
    public async Task ExecuteAsync(PaymentTimeoutArgs args, CancellationToken ct)
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

        s.On<OrderPlaced>().Hangfire("payment-timeout", (e, client, sp, ct) =>
        {
            client.Create(
                Job.FromExpression<PaymentTimeoutJob>(
                    job => job.ExecuteAsync(new PaymentTimeoutArgs(e.Data.OrderId, e.Id), CancellationToken.None)),
                new ScheduledState(TimeSpan.FromMinutes(15)));
            return ValueTask.CompletedTask;
        });
    });
});
```

### Quartz

Applications remain responsible for configuring Quartz storage and hosted service lifetime.

```csharp
using EventStoreCore;
using EventStoreCore.Postgres;
using EventStoreCore.Quartz;
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

### TickerQ

TickerQ uses in-memory storage by default. For durable delayed work, applications must configure a persistence package such as `TickerQ.EntityFrameworkCore` or Redis-backed storage, in addition to TickerQ host startup behavior.

```csharp
using EventStoreCore;
using EventStoreCore.Postgres;
using EventStoreCore.TickerQ;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Entities;

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

## Global event-log reads

Use `IEventLogReader` to consume persisted events across every stream in
ascending global sequence order. `ExistingDbContext<TDbContext>()` registers a
scoped reader for dependency injection, and the same reader is available from
`dbContext.EventLog`.

```csharp
var options = new EventLogReadOptions
{
    AfterSequence = checkpoint,
    TenantId = tenantId,
    StreamTypes = ["orders"],
    EventTypes = ["order_created", "order_cancelled"],
    MaxCount = 500
};

await foreach (var @event in eventLogReader.ReadAsync(options, ct))
{
    await export.WriteAsync(@event, ct);
    checkpoint = @event.Sequence;
}
```

`AfterSequence` is exclusive. Each read captures the highest currently visible
unfiltered sequence, bounded by an explicit `ThroughSequence`. Explicit pages
return the effective bound as `HeadSequence`; pass the page's `NextSequence` as
the next `AfterSequence` and preserve `HeadSequence` as `ThroughSequence`.
Async enumeration performs that continuation automatically and excludes events
allocated a sequence above its initial bound.

The reader materializes aliases and schema upcasters exactly like stream reads.
It does not persist consumer checkpoints or make side effects atomic with
checkpoint storage; custom consumers should remain idempotent. Database
sequences are allocated before their transactions commit, so concurrently
in-flight appends can become visible out of allocation order. Live consumers
that require lossless catch-up should reread an overlap and deduplicate by
`IEvent.Id`; do not treat an empty filtered page's `HeadSequence` as a strict
commit fence while append transactions are in flight.

Existing databases require an application-owned migration for the new unique
`Events.Sequence` index and the `(TenantId, Sequence)`,
`(StreamType, Sequence)`, and `(TypeName, Sequence)` read indexes.

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

The complete persisted stream identity is `(Id, StreamType, TenantId)`. Do not
apply the stream-type migration using only the stream ID and type in a
multi-tenant store.

1. Add `StreamType` (NOT NULL, default empty string) and, if it is not already present, `TenantId` (NOT NULL, default `Guid.Empty`) to both the `Streams` and `Events` tables.
2. Backfill both columns before changing constraints.
3. Update the primary key on `Streams` to `(Id, StreamType, TenantId)`.
4. Update the primary key on `Events` to `(StreamId, StreamType, TenantId, Version)`.
5. Update the foreign key relationship between `Events` and `Streams` to include both `StreamType` and `TenantId`.
6. Update lookup and ordering indexes to include the complete identity where appropriate.

**Note**: Changing primary keys in existing databases requires careful migration planning. Consider the impact on your application and data before applying these changes.

## Provider setup and ownership

PostgreSQL and SQL Server applications own their `DbContext`, connection,
transactions, and EF Core migrations. Configure the model with the selected
provider package's `UseEventStore()` extension, then register the same context
with `ExistingDbContext<TDbContext>()`. The provider packages only configure the
EventStoreCore model; they do not create or migrate a separate database.

- PostgreSQL stores event and snapshot JSON in `jsonb`.
- SQL Server stores event and snapshot JSON in `nvarchar(max)`.
- Both providers use `(Id, StreamType, TenantId)` as stream identity and enforce
  event versions within that identity.
- `EventId` values are generated GUIDs with a uniqueness constraint. Treat them
  as stable deduplication keys, not chronological or sequential values.
- Global event-log reads use the generated `Events.Sequence` value as their
  stable cross-stream cursor.
- Inline projections share the caller's EF Core transaction. Subscriptions and
  eventual projections are at-least-once.
- Daemon locks and provider-specific database migrations remain
  application-owned.

See the package READMEs for provider-specific setup and limitations.

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

## Stream lifecycle and governance

EventStoreCore supports explicit stream archive and tombstone metadata without deleting or rewriting
events. Archived streams remain readable but reject writes; tombstoned streams behave as not found
to normal stream reads and reject writes. Historical events retain their global positions and remain
available to projections, subscriptions, and global log readers.

Administrative transitions are tenant-scoped, require an exact stream version plus actor and reason,
and write immutable audit entries. Archive can be restored; tombstone is terminal. This is not
physical erasure, retention, or payload redaction.

See [Stream lifecycle and governance](docs/stream-lifecycle-and-governance.md) for the complete read,
append, concurrency, daemon, tenant, audit, and migration contract.

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
