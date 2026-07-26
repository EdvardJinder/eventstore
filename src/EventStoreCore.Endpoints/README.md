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

Projection rebuild endpoints accept the optional `tenantId` query parameter. Shadow rebuilds
can be abandoned with `POST /projections/{name}/rebuild/cancel`; cancelling the HTTP request
alone does not discard durable rebuild progress.
Entity-outbox routes resolve `IOutboxSubscriptionManager` and are available
under `/outbox-subscriptions`. The package does not add authentication or
authorization automatically; applications must secure the returned route
group.
