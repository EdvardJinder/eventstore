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

`AppendOperation` is the compact write request for retry-safe appends.
`AppendResult` reports the committed version range and stable event identities
without returning a materialized stream. `WithEventId` and `WithMetadata`
compose an `EventToAppend` envelope in either order. Operation-level
idempotency requires an event-store implementation that overrides the default
contract; the EF Core implementation in `EventStoreCore` supports it
transactionally.

`IEventLogReader`, `EventLogReadOptions`, and `EventLogPage` define portable
global-sequence paging. The `EventStoreCore` package supplies the EF Core
implementation and registers it through `ExistingDbContext<TDbContext>()`.
