# EventStoreCore

EventStoreCore provides event sourcing, projections, subscriptions, scheduler-backed delayed work, and a standalone EF entity outbox for .NET applications.

## Install

```bash
dotnet add package EventStoreCore
```

Add the provider packages you need:

- `EventStoreCore.Postgres` or `EventStoreCore.SqlServer`
- `EventStoreCore.Hangfire` for Hangfire-backed delayed work
- `EventStoreCore.Quartz` for Quartz-backed delayed work
- `EventStoreCore.TickerQ` for TickerQ-backed delayed work

Applications own their `DbContext`, database connection, and EF Core migrations.
Call the selected provider's `UseEventStore()` from `OnModelCreating`, then call
`ExistingDbContext<TDbContext>()` during EventStoreCore registration. Inline
appends and projections use that context and transaction. Daemon lock
infrastructure remains application-owned.

## Highlights

- Inline and eventual projections
- Subscription daemons with checkpointing
- Atomic domain-event capture from ordinary EF entities
- Standalone outbox reader and independently checkpointed outbox subscriptions
- Stable outbox subscription identities and recovery/replay management
- Replay-aware scheduler integrations
- Provider packages for common infrastructure

## Behavioral guarantees

- Stream identity consists of stream ID, stream type, and tenant ID.
- Event versions are ordered within that complete stream identity.
- Event IDs are generated GUIDs with a uniqueness constraint; their values are
  stable deduplication keys, not a source of ordering.
- Inline projections participate in the append transaction.
- Subscriptions and eventual projections are at-least-once and consumers must be
  idempotent.
- Provider-specific storage types and migration considerations are documented by
  the PostgreSQL and SQL Server packages.

## Bounded stream reads

Use `ReadPageAsync` for explicit pages or `ReadAsync` for cancellation-aware
asynchronous enumeration. `StreamReadOptions` supports inclusive forward and
backward version boundaries. Each page first captures the current stream
version, and its event query excludes later appends. Events are ordered by
stream version; ranges outside the stream are empty. A missing stream returns
`null` from `ReadPageAsync` and produces an empty asynchronous sequence.

Paged reads always read persisted events and do not use aggregate snapshots.
`FetchForReadingAsync<TState>` remains the state-rehydration API and may use a
compatible snapshot. Historical typed reads only use a snapshot whose stream
version is not newer than the requested historical version.

## Event metadata

Wrap a payload with `payload.WithMetadata(new EventMetadata(...))` to persist
correlation ID, causation ID, actor, and application headers. Read events expose
those immutable values plus the authoritative logical event type, stream type,
tenant, stream version, and global sequence. Transport integrations should map
reserved values to their native correlation/causation fields where available
and preserve application headers without treating transport-specific concepts
as Core metadata.

Existing rows remain compatible with null correlation, causation, and actor
values, `{}` headers, and schema version `1`. Migrations should add those event
columns with those defaults before deploying writers that use metadata.

### Propagation conventions

Core metadata remains transport-neutral. Integration code should apply these
stable mappings and preserve values rather than generating new identities at
each hop:

| EventStoreCore | CloudEvents | MassTransit | Durable Task input/envelope |
|---|---|---|---|
| `IEvent.Id` | `id` | `MessageId` or a dedicated source-event header | source event ID used for activity/orchestration deduplication |
| `CorrelationId` | `correlationid` extension | `CorrelationId` | application correlation ID |
| `CausationId` | `causationid` extension | `InitiatorId` or a dedicated causation header | triggering event/message ID |
| `Actor` | `actor` extension | application header | application actor field |
| application headers | non-conflicting extension attributes | application headers | serialized application headers |
| logical event type/schema version | `type` plus `schemaversion` | message type plus schema-version header | explicit event type and schema-version fields |
| stream type, tenant, stream version, global sequence | extension attributes | headers | explicit ordering and tenant fields |

Reserved transport fields must not be copied into application headers. Replay
may deliver the same source event again, so downstream publishers and workflow
starters should use `IEvent.Id` as the stable deduplication key. Correlation IDs
group a business operation; they are not delivery-deduplication keys.

## Serialization and schema evolution

Event and snapshot payloads use `IEventStoreSerializer`. Configure a replacement
with `UseSerializer` or configure the default JSON implementation with
`UseSystemTextJson`. A format change must remain backward-readable or be paired
with explicit schema-version changes.

Register logical event names and current schema versions with
`AddEvent<T>("logical_name", schemaVersion, ...)`. Version upcasters form an
explicit chain; each source version can have exactly one next step, steps run in
ascending version order, and missing steps fail materialization. Snapshot
registrations also have a schema version. A mismatch rebuilds from events by
default, or can throw when the application wants to run an explicit snapshot
migration first.

Database migrations should add `Events.SchemaVersion` and
`Snapshots.SchemaVersion` as non-null integers defaulting to `1`, alongside the
metadata columns described above. Backfill and validate historical fixtures
before removing defaults.

See the repository README for the full setup and examples:

- [Repository README](https://github.com/EdvardJinder/eventstore/blob/main/README.md)
