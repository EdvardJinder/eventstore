# EventStoreCore.SDK

Refit client contracts for the HTTP admin API exposed by
`EventStoreCore.Endpoints`.

```csharp
services.AddEventStoreEndpointsClient(client =>
{
    client.BaseAddress = new Uri("https://service.example/api/eventstore/admin/");
});
```

The application owns authentication headers, resilience policies, and base
address configuration. Tenant-scoped operations use the same optional tenant ID
query parameter as the server endpoints.
