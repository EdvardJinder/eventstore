# EventStoreCore.Abstractions

Provider-neutral contracts for EventStoreCore events, streams, projections,
subscriptions, entity-outbox management, checkpoints, metadata, serialization,
bounded stream reads, and global event-log reads.

```bash
dotnet add package EventStoreCore.Abstractions
```

Most applications should install `EventStoreCore` plus a persistence provider.
Reference this package directly when a domain or integration assembly needs only
contracts and should not depend on EF Core or daemon implementations.

The package contains interfaces and DTOs only. It does not register services,
configure persistence, or start background workers.

`IEventLogReader`, `EventLogReadOptions`, and `EventLogPage` define portable
global-sequence paging. The `EventStoreCore` package supplies the EF Core
implementation and registers it through `ExistingDbContext<TDbContext>()`.
