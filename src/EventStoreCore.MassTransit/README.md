# EventStoreCore.MassTransit

Publishes transformed EventStoreCore subscription events through MassTransit.

```csharp
services.AddEventStore(builder =>
{
    builder.AddMassTransitEventStoreSubscription(options =>
    {
        options.AddEvent<OrderCreated, OrderCreatedMessage>(
            e => new OrderCreatedMessage(e.Data.OrderId, e.Id));
    });
});
```

Configure the MassTransit bus and transport normally. EventStoreCore invokes the
publisher through its at-least-once subscription pipeline; message consumers
must remain idempotent and should preserve the source `EventId` for
deduplication. MassTransit owns transport topology, delivery, and retries.

## Entity outbox

```csharp
services.AddMassTransitOutboxSubscription(options =>
{
    options.AddEvent<OrderCreated, OrderCreatedMessage>(
        e => new OrderCreatedMessage(e.Data.OrderId, e.Id));
});
```

Outbox transformations receive `IOutboxEvent<T>`. Published messages use the
stable outbox event ID as the MassTransit `MessageId` and include
`EventStore-TenantId`, `EventStore-OutboxSequence`,
`EventStore-SourceEntityType`, `EventStore-SourceEntityKey`, and
`EventStore-EntityChangeKind` headers.
