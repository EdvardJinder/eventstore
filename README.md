# EJ.EventStore

## Install

```bash
dotnet add package EJ.EventStore
dotnet add package EJ.EventStore.Persistence.EntityFrameworkCore
dotnet add package EJ.EventStore.Persistence.EntityFrameworkCore.Postgres
```

## Quick start

```csharp
using EJ.EventStore;
using EJ.EventStore.Persistence.EntityFrameworkCore;
using EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;
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


