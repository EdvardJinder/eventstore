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
2. Deploy the library update and run `await dbContext.BackfillEventTypeNamesAsync()` to populate missing values.
3. Optionally tighten constraints (remove the default or enforce non-empty values) once backfill is complete.

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
