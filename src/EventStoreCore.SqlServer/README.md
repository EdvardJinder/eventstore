# EventStoreCore.SqlServer

SQL Server persistence configuration for EventStoreCore using EF Core.

## Setup

Register your application `DbContext`, configure the EventStore model in
`OnModelCreating`, and tell EventStoreCore to reuse that context:

```csharp
using EventStoreCore;
using EventStoreCore.SqlServer;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseEventStore();
    }
}

services.AddDbContext<AppDbContext>(
    options => options.UseSqlServer(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<AppDbContext>();
});
```

`ExistingDbContext<TDbContext>()` does not create or migrate a separate
database. EventStoreCore uses the application-owned context, connection,
transaction, and migrations. Add migrations from the application project after
calling `UseEventStore()`.

## Provider behavior

- Event payloads and snapshots use SQL Server `nvarchar(max)` columns.
- Stream identity is `(Id, StreamType, TenantId)`.
- Event ordering within a stream is protected by the
  `(StreamId, StreamType, TenantId, Version)` key.
- `EventId` is a generated or caller-supplied GUID with a global uniqueness
  constraint; consumers must not rely on GUID ordering.
- Idempotent append operation keys are globally unique through the
  `AppendOperations` primary key. The operation record and events commit in the
  same transaction.
- Global event-log reads use the unique `Events.Sequence` index plus filtered
  sequence indexes for tenant, logical stream type, and logical event type.
- Inline projections share the append transaction. Subscription and eventual
  projection delivery is at-least-once.
- Daemons require an application-provided `IDistributedLockProvider`.

Provider-specific database migrations remain application owned. Review generated
migrations when upgrading EventStoreCore, especially changes to keys, indexes,
or required metadata columns. Existing databases adding global event-log reads
need a migration for the new event sequence indexes.

When upgrading for idempotent writes, generate and deploy an application
migration before enabling new writers. It must create `AppendOperations` with a
`uniqueidentifier` primary key `IdempotencyKey`, required `nvarchar(64)`
`RequestHash`, stream ID/type/tenant columns, previous/current `bigint`
versions, and a `datetimeoffset` timestamp. Add the model-generated lookup
index over `(StreamId, StreamType, TenantId, CurrentVersion)`. Verify that the
existing global unique constraint on `Events.EventId` is present; no event
backfill is required. Deploy schema first, then writers, and retain operation
rows for at least the maximum retry lifetime.
