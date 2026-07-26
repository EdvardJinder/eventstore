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
