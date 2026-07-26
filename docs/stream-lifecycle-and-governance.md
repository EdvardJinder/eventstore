# Stream lifecycle and governance

EventStoreCore stream lifecycle is metadata governance, not event deletion. It preserves the
append-only event log and gives applications explicit archive and tombstone semantics around the
complete stream identity `(StreamType, StreamId, TenantId)`.

## States and API behavior

| State | Normal stream reads | Appends and writable fetches | Global log, projections, subscriptions | Administrative metadata |
|---|---|---|---|---|
| `Active` | Returned | Allowed | Unchanged | Visible |
| `Archived` | Returned with `LifecycleState == Archived` | Rejected | Unchanged | Visible |
| `Tombstoned` | Behaves as not found | Rejected | Historical events remain visible and deliverable | Visible |

Archive is reversible through the administrative lifecycle API. Tombstone is terminal. Neither
operation changes stream event versions, event global sequence positions, event payloads, or
snapshots. A tombstone is therefore not a privacy erasure or payload redaction feature.

Lifecycle metadata is deliberately not represented as a domain event. Existing projection and
subscription checkpoints continue over the same immutable event log, and lifecycle changes do not
cause a projection or subscription callback. Consumers that schedule delayed work should re-check
the current stream lifecycle before producing a new append.

## Administrative transitions

Use the scoped `IStreamLifecycleManager` registered by `ExistingDbContext<TDbContext>()`, or the
lifecycle manager exposed by an EventStore-enabled `DbContext`. Every transition requires the exact
current event version plus an actor and reason:

```csharp
await dbContext.StreamLifecycle.ArchiveAsync(
    "orders",
    orderId,
    tenantId,
    expectedVersion: 42,
    new StreamLifecycleChange
    {
        Actor = "retention-service",
        Reason = "Order reached the configured archive age.",
        CorrelationId = governanceRunId
    },
    cancellationToken);
```

`RestoreAsync` permits only `Archived -> Active`. `TombstoneAsync` permits `Active -> Tombstoned`
or `Archived -> Tombstoned`. Tombstoned streams cannot be restored or recreated under the same
identity. Invalid transitions and stale versions throw `StreamLifecycleConflictException`.

`GetAsync` is the explicit administrative visibility boundary. It returns current metadata and the
immutable transition history for active, archived, and tombstoned streams, but does not create a
separate event-payload bypass. Protect lifecycle access as an administrative capability in the
application; EventStoreCore does not infer authorization from actor strings.

Concurrent appends and lifecycle transitions use provider-neutral EF optimistic concurrency on the
stream version and lifecycle state. Exactly one competing operation commits. Normal append
expected-version semantics remain in force, and appending to archived or tombstoned streams throws
`StreamNotWritableException`. Persist pending event-store writes before invoking a lifecycle
transition on the same `DbContext`; the manager rejects a context with pending streams, events, or
snapshots so governance metadata cannot accidentally commit an in-memory append.

## Tenant boundaries

Every lifecycle operation requires the tenant identifier. A transition affects only the exact
`(StreamType, StreamId, TenantId)` identity. The administrative audit row repeats that complete
identity so tenant-scoped operational queries do not need to infer ownership from an event payload.

## Schema migration

Generate and review an application-owned EF Core migration after upgrading:

```bash
dotnet ef migrations add AddEventStoreStreamLifecycle
dotnet ef database update
```

The migration adds `Streams.LifecycleState` with an `Active` default and creates the
`StreamLifecycleEntries` audit table. Existing streams therefore remain active. Provider migration
templates are shipped in the `migrations/` folder of the `EventStoreCore.Postgres` and
`EventStoreCore.SqlServer` packages for applications that manage SQL directly:

- `EventStoreCore.Postgres/Migrations/20260726_StreamLifecycle.sql`
- `EventStoreCore.SqlServer/Migrations/20260726_StreamLifecycle.sql`

Apply either template exactly once and review identifier lengths and naming conventions against the
application's existing EF migration history. The concurrency-token change does not add a database
column beyond `LifecycleState`; it changes generated `UPDATE` predicates.

## Retention, purge, and redaction boundary

This slice intentionally provides no age-based retention daemon, physical purge, or payload
redaction API. Those operations would alter audit evidence, projection rebuild inputs, subscription
replay, snapshots, and provider-specific storage. They require a separate explicit design with
authorization, durable audit evidence, checkpoint coordination, snapshot treatment, provider
transactions, and failure recovery. Do not implement retention by deleting `Streams`, `Events`, or
`Snapshots` directly.
