# EventStoreCore.Endpoints

Minimal ASP.NET Core admin endpoints for projection, subscription, and
entity-outbox subscription status, pause/resume, retry/skip, replay, and rebuild
operations.

```csharp
var admin = app.MapGroup("/api/eventstore/admin")
    .MapEventStoreApiEndpoints();

admin.RequireAuthorization("eventstore-admin");
```

The endpoints resolve `IProjectionManager` and `ISubscriptionManager` from DI.
Entity-outbox routes resolve `IOutboxSubscriptionManager` and are available
under `/outbox-subscriptions`. The package does not add authentication or
authorization automatically; applications must secure the returned route
group.
