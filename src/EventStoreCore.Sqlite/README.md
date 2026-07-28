# EventStoreCore.Sqlite

SQLite persistence configuration for EventStoreCore using EF Core.

## Setup

Register your application `DbContext`, configure the EventStore model in
`OnModelCreating`, and tell EventStoreCore to reuse that context:

```csharp
using EventStoreCore;
using EventStoreCore.Sqlite;
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
    options => options.UseSqlite(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<AppDbContext>();
});
```

`ExistingDbContext<TDbContext>()` does not create or migrate a separate
database. EventStoreCore uses the application-owned context, connection,
transaction, and migrations. Add migrations from the application project after
calling `UseEventStore()`.

Applications using the standalone entity outbox should also call
`modelBuilder.UseEntityOutbox()` on the context that owns its tables.

SQLite in-memory databases exist only while their connection remains open. Keep
the connection alive for the full test or application scope when using
`Data Source=:memory:`.

## Provider behavior

- Event payloads, headers, snapshots, and entity-outbox JSON use `TEXT`
  columns.
- `DateTimeOffset` values are normalized to UTC and stored as integer ticks so
  timestamp replay filters and ordering translate in SQLite.
- Stream identity is `(Id, StreamType, TenantId)`.
- Event ordering within a stream is protected by a unique
  `(StreamId, StreamType, TenantId, Version)` index.
- The integer `Events.Sequence` primary key provides the generated global event
  position required by SQLite.
- SQLite permits only one database writer at a time, so generated sequences
  cannot commit out of allocation order and no additional application lock is
  installed. A competing writer can receive `SQLITE_BUSY` after the configured
  busy timeout; applications should retry that transaction.
- `EventId` is a generated GUID with a global uniqueness constraint; consumers
  must not rely on GUID ordering.
- Inline projections share the append transaction. Subscription and eventual
  projection delivery is at-least-once.
- Daemons require an application-provided `IDistributedLockProvider`. Select an
  implementation appropriate for every process that shares the database.

Provider-specific database migrations remain application owned. SQLite has
limited `ALTER TABLE` support, so review generated table-rebuild migrations
when upgrading EventStoreCore.
