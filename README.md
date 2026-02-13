# EventStoreCore

## Install

```bash
dotnet add package EventStoreCore
dotnet add package EventStoreCore.Postgres
# or EventStoreCore.SqlServer
```


## Quick start

```csharp
using EventStoreCore;
using EventStoreCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class MyEventStoreDbContext : DbContext
{
    public MyEventStoreDbContext(DbContextOptions<MyEventStoreDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEventStore();
    }
}

var services = new ServiceCollection();

services.AddDbContext<MyEventStoreDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddEventStore(builder =>
{
    builder.ExistingDbContext<MyEventStoreDbContext>();
    builder.AddProjection<MyEventStoreDbContext, MyProjection, MySnapshot>(
        ProjectionMode.Inline,
        options => options.Handles<MyEvent>());
});
```

Projection and subscription daemons require an `IDistributedLockProvider`. Register any implementation (Redis, SQL Server, Postgres, etc.) in DI.

## Event type names

EventStore now persists a logical event type name in `DbEvent.TypeName`. By default, it uses snake_case based on the CLR type name (for example, `UserCreated` becomes `user_created`).

- Register custom names when needed: `builder.AddEvent<UserCreated>("user_created_v2")`.
- If you do not register an event, the default snake_case name is used automatically on write.
- Event materialization throws `EventMaterializationException` if the event type cannot be resolved.

### Migration steps

1. Add a `TypeName` column (NOT NULL, default empty string) to the `Events` table.
2. Populate `TypeName` for existing rows using your preferred backfill process.
3. Optionally tighten constraints (remove the default or enforce non-empty values) once values are populated.

## Stream types

EventStore supports multiple streams with the same ID but different types, enabling scenarios like:
- Document upload/lifecycle stream and document analysis stream sharing the same document ID
- Order processing stream and order audit stream sharing the same order ID

Stream type is specified as the first parameter when calling `IEventStore` methods:

```csharp
// Create different stream types with the same ID
var docId = Guid.NewGuid();
eventStore.StartStream("document-lifecycle", docId, events: [new DocumentCreated()]);
eventStore.StartStream("document-analysis", docId, events: [new AnalysisStarted()]);

// Fetch specific stream types
var lifecycleStream = await eventStore.FetchForReadingAsync("document-lifecycle", docId);
var analysisStream = await eventStore.FetchForReadingAsync("document-analysis", docId);

// Default stream type (empty string)
eventStore.StartStream(docId, events: [new SomeEvent()]);
var stream = await eventStore.FetchForReadingAsync(docId);
```

**Default behavior**: Overloads without `streamType` default to an empty string `""`, maintaining backwards compatibility.

### Migration steps for existing databases

1. Add a `StreamType` column (NOT NULL, default empty string) to both the `Streams` and `Events` tables.
2. Update the primary key on `Streams` from `Id` to `(Id, StreamType)`.
3. Update the primary key on `Events` from `(StreamId, Version)` to `(StreamId, StreamType, Version)`.
4. Update the foreign key relationship between `Events` and `Streams` to include `StreamType`.
5. Update indexes to include `StreamType` where appropriate.

**Note**: Changing primary keys in existing databases requires careful migration planning. Consider the impact on your application and data before applying these changes.

## Project guidelines

- Keep public APIs small, composable, and backwards compatible.
- Document all `public` types and members with XML docs.
- Favor explicit configuration over magic defaults; surface options via builders.
- Keep EF Core provider logic isolated to provider-specific projects.
- Projections and subscriptions should be deterministic and idempotent.
- Add tests for new behaviors using `EventStoreCore.Testing` helpers.

## Testing



Install the test helpers:

```bash
dotnet add package EventStoreCore.Testing
```

Behavior-style tests call your stream extension methods directly. `Given` seeds history, `When` appends new events, and `Then` asserts only the new events in order.

```csharp
using EventStoreCore.Testing;

public sealed class ItemTypeTests : StreamBehaviorTest<ItemTypeState>
{
    [Fact]
    public void create_emits_created()
    {
        When(s => s.Create(itemTypeId, mspId, clientId, "Widget", "desc"));

        Then(new ItemTypeCreated(itemTypeId, mspId, clientId, "Widget", "desc"));
    }
}
```
