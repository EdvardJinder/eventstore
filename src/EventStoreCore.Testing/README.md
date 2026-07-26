# EventStoreCore.Testing

Behavior-style helpers for testing event-sourced stream commands without an
external database.

```csharp
public sealed class OrderTests : StreamBehaviorTest<OrderState>
{
    [Fact]
    public void place_emits_order_placed()
    {
        When(stream => stream.Place(orderId));
        Then(new OrderPlaced(orderId));
    }
}
```

`Given` seeds history, `When` executes stream behavior, `Then` compares only new
events in order, and `ThenState` asserts reconstructed state. These helpers are
for application tests and do not replace provider-backed persistence contracts.
