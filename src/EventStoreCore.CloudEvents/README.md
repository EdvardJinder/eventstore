# EventStoreCore.CloudEvents

Maps EventStoreCore subscription events to CloudEvents and delivers them through
an application-provided `ICloudEventSubscription`.

```csharp
services.AddEventStore(builder =>
{
    builder.AddCloudEventSubscription<MyCloudEventPublisher>(options =>
    {
        options.MapEvent<OrderCreated>(
            "com.example.order.created",
            "urn:example:orders",
            e => e.StreamId.ToString("D"));
    });
});
```

The integration uses the regular at-least-once subscription pipeline. Publisher
implementations must be idempotent and should use the source EventStore
`EventId` as the delivery-deduplication key. EventStoreCore owns checkpointing;
the publisher owns transport authentication, retries, and destination behavior.

## Entity outbox

Map ordinary EF entity-outbox events through the same publisher contract:

```csharp
services.AddCloudEventOutboxSubscription<MyCloudEventPublisher>(options =>
{
    options.MapEvent<OrderCreated>(
        "com.example.order.created",
        "urn:example:orders",
        e => e.SourceEntityKey);
});
```

Outbox mappings receive `IOutboxEvent<T>`. The adapter sets the CloudEvent ID
from the stable outbox event ID and adds `tenantid`, `outboxsequence`,
`sourceentitytype`, `sourceentitykey`, and `entitychangekind` extension
attributes. Set `preserveCloudEventId: true` on the custom mapping overload only
when the supplied ID is itself a stable deduplication key.
