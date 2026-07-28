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
- Event ordering within a stream is protected by a unique
  `(StreamId, StreamType, TenantId, Version)` index.
- `EventId` is a generated GUID with a global uniqueness constraint; consumers
  must not rely on GUID ordering.
- Global event-log reads use the generated `Events.Sequence` primary key plus
  filtered sequence indexes for tenant, logical stream type, and logical event
  type.
- Inline projections share the append transaction. Subscription and eventual
  projection delivery is at-least-once.
- Daemons require an application-provided `IDistributedLockProvider`.

Provider-specific database migrations remain application owned. Review generated
migrations when upgrading EventStoreCore, especially changes to keys, indexes,
or required metadata columns. Existing databases need a migration that makes
`Events.Sequence` the generated primary key and adds the unique stream-version
index.
