# Daemon identities and filtering

Projection and subscription checkpoint identities are operational data. Assign explicit names to long-lived
registrations so moving or renaming a CLR type does not create a new checkpoint:

```csharp
builder.AddProjection<MyDbContext, OrdersProjection, OrderView>(
    ProjectionMode.Eventual,
    options =>
    {
        options.Name("orders");
        options.Handles<OrderCreated>();
        options.IncludeLogicalEventType("order_created");
        options.IncludeStreamType("orders");
        options.IncludeTenant(tenantId);
    });

builder.AddSubscription<OrderNotifications>(options =>
{
    options.Name = "order-notifications";
    options.IncludeLogicalEventType("order_created");
    options.IncludeStreamType("orders");
    options.UnknownEventPolicy = UnknownEventPolicy.Skip;
});
```

For compatibility, unnamed projections use the projection's full CLR type name and unnamed subscriptions use
the subscription's assembly-qualified CLR type name. Renaming, moving, or changing the assembly of an unnamed
handler therefore creates a new checkpoint and replays matching history.

To rename an existing registration without replaying it:

1. Stop every daemon instance.
2. Update the persisted `ProjectionName` or `SubscriptionAssemblyQualifiedName` checkpoint key, including every
   tenant-scoped row.
3. Configure the new explicit name in application startup.
4. Restart the daemons and verify status through the same logical name.

Logical names must be unique; duplicate registrations fail during configuration or daemon startup. The same
name is used for checkpoints, distributed locks, status APIs, logs, traces, and metrics.

Entity-outbox subscriptions follow the same identity rule:

```csharp
services.AddOutboxSubscription<PublishOrders>(
    options => options.Name = "publish-orders");
```

Their checkpoints live in `OutboxSubscriptions`. Stop every outbox daemon and
update `SubscriptionAssemblyQualifiedName` before adopting a stable name for an
existing unnamed registration if replay is not desired.

## Typed and filtered subscriptions

Implement `ISubscription<TEvent>` and register it with both type parameters to receive typed payloads without
losing `IEvent<TEvent>` metadata:

```csharp
builder.AddSubscription<OrderCreatedHandler, OrderCreated>(options =>
{
    options.Name = "order-created";
    options.IncludeTenant(tenantId);
});
```

Filters support logical event type, CLR type, stream type, stream ID, and tenant. Multiple values within one
category are ORed, while categories are ANDed. Filtered-out events always advance the checkpoint; replay uses
the same filtering rules in both global and tenant-scoped daemons.

Unmaterializable events fail and enter the normal retry flow by default. `Skip` advances the checkpoint,
`Quarantine` immediately dead-letters the checkpoint, and `HandleUnknown` invokes an application callback with
raw persisted metadata before advancing. Changing a logical event type registration does not break a logical
type filter as long as the persisted `TypeName` remains stable.

Entity-outbox registrations support logical event type, CLR event type, tenant,
source entity type, and entity change-kind filters with the same OR-within and
AND-between behavior. They also support the same fail, skip, quarantine, and
custom unknown-event policies.

## Filtered projections

Projection filters support CLR event type, logical event type, stream type, stream ID, and tenant. Multiple
values within one category are ORed, while categories are ANDed. `Handles<T>` values form the CLR type
category, and `Ignores<T>` can exclude types from either `HandlesAll` or an explicit CLR allow-list.

Inline and eventual projections use the same matching rules. Eventual projections apply logical event,
stream type, stream ID, and tenant predicates in SQL before the batch limit. CLR checks remain
post-materialization so aliases and upcasters retain their normal behavior. Filtered events still advance the
checkpoint; when no more matching rows remain, the daemon advances through filtered rows at the captured log
head.

Persisted filters are evaluated before materialization, so excluded unknown events do not fail a projection.
Matching unknown events retain the existing policy: `HandlesAll` fails by default, `IgnoreUnknown` skips, and
an explicit `Handles<T>` allow-list skips an unresolvable CLR type because it cannot match that list.

## Telemetry and time

Daemons use the DI-provided `TimeProvider` when available, falling back to `TimeProvider.System`. They publish
activities and metrics under `EventStoreCore.Daemons`, including batch duration, processed and failed events,
retries, checkpoint lag, and lock contention. Register one or more `IDaemonFaultObserver` implementations to
receive structured projection fault and subscription fault/dead-letter transitions.

Entity-outbox dispatch uses the same diagnostic sources with daemon kind
`outbox-subscription`.
