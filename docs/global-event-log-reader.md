# Global event-log reader

`IEventLogReader` provides a provider-neutral catch-up read across all persisted
streams. Resolve it from dependency injection after
`ExistingDbContext<TDbContext>()`, or access the same implementation through
`dbContext.EventLog`.

## Cursor model

- `AfterSequence` is an exclusive lower bound.
- `ThroughSequence` is an optional inclusive upper bound.
- A read with no explicit upper bound first captures the highest currently
  committed unfiltered global sequence.
- `EventLogPage.HeadSequence` contains that commit high-water mark, lowered by
  an explicit `ThroughSequence` when supplied.
- `EventLogPage.NextSequence` is the exclusive lower bound for the next page.

For explicit paging, preserve the first page's `HeadSequence` as
`ThroughSequence` and move `AfterSequence` to `NextSequence`. `ReadAsync`
handles this automatically and therefore never includes events appended after
enumeration starts.

The head is intentionally unfiltered, which is useful for bounded rebuilds and
exports.

## Filters

Filtering happens in the database before page limits are applied:

- one tenant ID;
- one or more logical stream types;
- one or more logical event types.

Null or empty stream/event type collections mean no filter. Results are always
ordered by ascending global sequence. Materialization uses the configured
serializer, event aliases, and schema upcasters.

## Delivery responsibility

The reader does not own a durable checkpoint and does not coordinate consumer
side effects. A custom catch-up consumer should store its checkpoint only after
its side effect succeeds and remain idempotent if those two writes cannot share
a transaction.

PostgreSQL and SQL Server contexts registered through
`ExistingDbContext<TDbContext>()` acquire a provider transaction-scoped lock
before allocating event or entity-outbox sequences and hold it through commit.
Consequently, a later transaction cannot commit an event at or below an observed
`HeadSequence`; the head is a strict commit fence for participating writers.
Rolled-back transactions can leave harmless sequence gaps.

All writers to the database must participate. During upgrade, quiesce writers,
deploy the updated registration everywhere, and only then restart checkpointed
consumers. An older process or direct SQL writer can bypass the lock and
invalidate the fence. Existing checkpoints that may already have skipped an
event should be replayed from a known-safe sequence, and affected eventual
projections should be rebuilt. The change requires no database schema migration
but serializes sequence-allocating transactions until they commit.

## Database migration

Applications own EF Core migrations. Existing databases should add:

- a generated primary key on `Events.Sequence`;
- a unique index on `(StreamId, StreamType, TenantId, Version)`;
- an index on `(TenantId, Sequence)`;
- an index on `(StreamType, Sequence)`;
- an index on `(TypeName, Sequence)`.
