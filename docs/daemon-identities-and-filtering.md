# Daemon identities and filtering

Projection and subscription checkpoint identities are operational data. Assign explicit names to long-lived
registrations so moving or renaming a CLR type does not create a new checkpoint:

```csharp
builder.AddProjection<MyDbContext, OrdersProjection, OrderView>(
    ProjectionMode.Eventual,
    options => options.Name("orders"));

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

## Telemetry and time

Daemons use the DI-provided `TimeProvider` when available, falling back to `TimeProvider.System`. They publish
activities and metrics under `EventStoreCore.Daemons`, including batch duration, processed and failed events,
retries, checkpoint lag, and lock contention. Register one or more `IDaemonFaultObserver` implementations to
receive structured projection fault and subscription fault/dead-letter transitions.
