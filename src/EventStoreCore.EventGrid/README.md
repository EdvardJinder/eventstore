# EventStoreCore.EventGrid

Azure Event Grid publisher integration built on `EventStoreCore.CloudEvents`.

```csharp
services.AddEventStore(builder =>
{
    builder.AddEventGridSubscription(options =>
    {
        options.MapEvent<OrderCreated>(
            "com.example.order.created",
            "urn:example:orders",
            e => e.StreamId.ToString("D"));
    });
});
```

Configure Azure credentials and the Event Grid client in application DI.
Delivery is at-least-once; consumers should deduplicate with the source
EventStore `EventId`. EventStoreCore owns the subscription checkpoint, while
Event Grid owns transport delivery and retry behavior.

## Entity outbox

```csharp
services.AddEventGridOutboxSubscription(options =>
{
    options.MapEvent<OrderCreated>(
        "com.example.order.created",
        "urn:example:orders",
        e => e.SourceEntityKey);
});
```

This uses the independently checkpointed entity-outbox daemon. The CloudEvent ID
is the stable outbox event ID, and source-entity, tenant, sequence, and change
metadata are included as CloudEvent extension attributes.
