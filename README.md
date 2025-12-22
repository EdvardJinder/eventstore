# EventStoreCore

## Install

```bash
dotnet add package EventStoreCore
dotnet add package EventStoreCore.Persistence.EntityFrameworkCore
dotnet add package EventStoreCore.Persistence.EntityFrameworkCore.Postgres
```

## Quick start

```csharp
using EventStoreCore;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
