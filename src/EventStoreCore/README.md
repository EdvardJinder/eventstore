# EventStoreCore

EventStoreCore provides event sourcing, projections, subscriptions, and scheduler-backed delayed work for .NET applications.

## Install

```bash
dotnet add package EventStoreCore
```

Add the provider packages you need:

- `EventStoreCore.Postgres` or `EventStoreCore.SqlServer`
- `EventStoreCore.Hangfire` for Hangfire-backed delayed work
- `EventStoreCore.Quartz` for Quartz-backed delayed work

## Highlights

- Inline and eventual projections
- Subscription daemons with checkpointing
- Replay-aware scheduler integrations
- Provider packages for common infrastructure

See the repository README for the full setup and examples:

- [Repository README](https://github.com/EdvardJinder/eventstore/blob/main/README.md)
