# EventStoreCore.Testing

Application-facing test helpers for stream behavior, projections, subscriptions,
schema evolution, optimistic concurrency, and hosted daemons. The package keeps
EventStoreCore's EF persistence rows and registrations out of application tests.

## Stream behavior

`Given` seeds history, `When` executes stream behavior, `Then` compares only new
events in order, and `ThenState` asserts reconstructed state.

```csharp
public sealed class OrderTests : StreamBehaviorTest<OrderState>
{
    [Fact]
    public void place_emits_order_placed()
    {
        Given(new CustomerRegistered(customerId));

        When(stream => stream.Place(orderId, customerId));

        Then(new OrderPlaced(orderId, customerId));
        ThenState(state => Assert.Equal(orderId, state.OrderId));
    }
}
```

## Projection evolution and rebuild

`TestEvent<T>` supplies an explicit, deterministic event envelope. The projection
harness calls the public `IProjection<TSnapshot>` contract directly and does not
infer snapshot keys or registration filters.

```csharp
var store = new Dictionary<Guid, AccountBalance>();
var harness = new ProjectionTestHarness<AccountBalanceProjection, AccountBalance>();
var events = new IEvent[]
{
    new TestEvent<AccountOpened>(
        new AccountOpened(10m),
        streamId: accountId,
        version: 1,
        sequence: 1,
        typeName: "account_opened",
        streamType: "accounts"),
    new TestEvent<FundsDeposited>(
        new FundsDeposited(5m),
        streamId: accountId,
        version: 2,
        sequence: 2,
        typeName: "funds_deposited",
        streamType: "accounts")
};

await harness.RebuildAsync(
    events,
    e => store.TryGetValue(e.StreamId, out var snapshot)
        ? snapshot
        : store[e.StreamId] = new AccountBalance(),
    cancellationToken);

Assert.Equal(15m, store[accountId].Balance);
Assert.Equal(2, harness.ProjectionVersion);
```

`RebuildAsync` always awaits `ClearAsync` before resolving the first snapshot and
replays events in enumeration order. Supply `providerState` to the constructor
when a projection intentionally uses an application DbContext or another local
persistence dependency through `IProjectionContext`.

## Subscription filters, retry, replay, and unknown events

The subscription harness uses the real EventStoreCore registration, daemon batch,
and manager behavior over an isolated in-memory event log. Filtered events advance
the checkpoint, retry redelivers the same event ID, and replay is inclusive.

```csharp
var handler = new PublishAccountEvents();
await using var harness = SubscriptionTestHarness.For(
    handler,
    registration =>
    {
        registration.Name = "publish-account-events";
        registration.IncludeEventType<FundsDeposited>();
        registration.IncludeStreamType("accounts");
        registration.IncludeTenant(tenantId);
    },
    daemon =>
    {
        daemon.BatchSize = 20;
        daemon.MaxRetryAttempts = 2;
        daemon.RetryDelay = TimeSpan.FromMinutes(1);
    },
    fakeTimeProvider);

harness.Given(
    new TestEvent<FundsDeposited>(
        new FundsDeposited(5m),
        id: eventId,
        streamId: accountId,
        tenantId: tenantId,
        sequence: 1,
        typeName: "funds_deposited",
        streamType: "accounts"));

await harness.ProcessUntilIdleAsync(cancellationToken: cancellationToken);

var status = await harness.GetStatusAsync(cancellationToken: cancellationToken);
Assert.Equal(1, status.Position);

await harness.ReplayAsync(
    startSequence: 1,
    cancellationToken: cancellationToken);
await harness.ProcessUntilIdleAsync(cancellationToken: cancellationToken);
```

Use the typed factory for `ISubscription<TEvent>`:

```csharp
await using var harness =
    SubscriptionTestHarness.For<PublishDeposits, FundsDeposited>(handler);
```

Unknown payloads can be seeded without referencing an internal EF row:

```csharp
UnknownEventContext? observed = null;
await using var harness = SubscriptionTestHarness.For(
    handler,
    options => options.HandleUnknown((context, _) =>
    {
        observed = context;
        return ValueTask.CompletedTask;
    }));

harness.GivenUnknown(
    logicalTypeName: "legacy_deposit",
    clrTypeName: "Legacy.Contracts.Deposit, Legacy.Contracts",
    json: """{"amount":5}""",
    sequence: 1);

await harness.ProcessUntilIdleAsync(cancellationToken: cancellationToken);
Assert.Equal("legacy_deposit", observed!.LogicalTypeName);
```

Set `UnknownEventPolicy` to `Skip`, `Fail`, or `Quarantine` to exercise those
paths. `GetFailedEventAsync`, `RetryFailedEventAsync`, `SkipFailedEventAsync`,
and `ReplayAsync` expose the same public management behavior applications use.

## Schema upcasters

The schema harness builds the real event-type registry and materializes the
result through the configured serializer.

```csharp
var harness = new SchemaUpcasterTestHarness<AccountOpened>(
    "account_opened",
    currentSchemaVersion: 3,
    eventType => eventType
        .AddUpcaster(1, 2, json => Rename(json, "openingBalance", "balance"))
        .AddUpcaster(2, 3, json => AddCurrency(json, "SEK")));

var current = harness.Upcast(
    """{"openingBalance":10}""",
    storedSchemaVersion: 1);

Assert.Equal("SEK", current.Currency);
```

Missing links, newer stored versions, and failing transformations surface the
same `EventMaterializationException` as runtime reads.

## Optimistic concurrency

Use the application's configured `IEventStore`; this keeps provider behavior
inside the provider rather than simulating it in the testing package.

```csharp
var harness = new OptimisticConcurrencyTestHarness(
    eventStore,
    streamType: "accounts",
    streamId: accountId,
    tenantId: tenantId);

await harness.AppendAsync(
    ExpectedVersion.NoStream,
    [new AccountOpened(10m)],
    cancellationToken);

var conflict = await harness.ExpectConflictAsync(
    ExpectedVersion.NoStream,
    [new AccountOpened(20m)],
    cancellationToken);

Assert.Equal(1, conflict.ActualVersion);
```

Use a provider-backed store and separate DbContexts for true simultaneous-writer
contract tests.

## Deterministic hosted daemons

Pass the same `FakeTimeProvider` to the EventStoreCore daemon and the harness.
Observe completion through application state or a public projection/subscription
manager rather than querying internal status rows.

```csharp
var clock = new FakeTimeProvider();
var daemon = services.GetRequiredService<SubscriptionDaemon<AppDbContext>>();
await using var harness = new DaemonTestHarness(daemon, clock);

await harness.StartAsync(cancellationToken);
await harness.RunUntilAsync(
    async ct =>
    {
        var status = await manager.GetStatusAsync("publish-account-events", ct);
        return status?.Position == expectedSequence;
    },
    advanceBy: TimeSpan.FromSeconds(10),
    maxAttempts: 20,
    ct: cancellationToken);
```

The in-memory stream and subscription fixtures validate application behavior,
not relational transactions, provider constraints, distributed locks, or
simultaneous-writer races. Keep provider-backed integration tests for those
contracts.
