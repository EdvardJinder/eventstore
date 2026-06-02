# EventStoreCore.Scheduling

Shared scheduling abstractions for EventStoreCore provider packages.

Most applications should reference a concrete provider package such as `EventStoreCore.Hangfire`, `EventStoreCore.Quartz`, or `EventStoreCore.TickerQ`.

## Contract

- `ISchedulerBuilder` provides the root `AddScheduler(...)` integration surface.
- `On<TEvent>()` starts provider-native action registration for an event type.
- Provider packages expose actions such as `.Hangfire(...)`, `.Quartz(...)`, and `.TickerQ(...)`.
- EventStoreCore invokes each provider action at most once for the same provider, registration name, tenant id, and EventStore `EventId`.
- The provider action receives a scoped service provider and owns scheduling, cancellation, replacement, trigger configuration, and job payload conventions.
- Use explicit action names for production integrations so replay identity survives refactors.

See the repository README for end-to-end provider examples:

- [Repository README](https://github.com/EdvardJinder/eventstore/blob/main/README.md)
