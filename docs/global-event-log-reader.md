# Global event-log reader

`IEventLogReader` provides a provider-neutral catch-up read across all persisted
streams. Resolve it from dependency injection after
`ExistingDbContext<TDbContext>()`, or access the same implementation through
`dbContext.EventLog`.

## Cursor model

- `AfterSequence` is an exclusive lower bound.
- `ThroughSequence` is an optional inclusive upper bound.
- A read with no explicit upper bound first captures the highest currently
  visible unfiltered global sequence.
- `EventLogPage.HeadSequence` contains that visible high-water mark, lowered by
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

Database sequences are allocated before the append transaction commits.
Concurrent transactions can therefore become visible out of allocation order.
For a live feed, reread a suitable sequence overlap and deduplicate by stable
`IEvent.Id`. In particular, an empty filtered page does not make
`HeadSequence` a strict commit fence while append transactions are in flight.

## Database migration

Applications own EF Core migrations. Existing databases should add:

- a unique index on `Events.Sequence`;
- an index on `(TenantId, Sequence)`;
- an index on `(StreamType, Sequence)`;
- an index on `(TypeName, Sequence)`.
