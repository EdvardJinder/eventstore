# EventStoreCore.Scheduling

Shared scheduling abstractions for EventStoreCore provider packages.

## What this package contains

- `ISchedulerBuilder` for registering scheduler-backed event mappings
- `ScheduleKey` as the stable business identity for one logical scheduled action
- `IScheduledJobHandler<TArgs>` for DI-resolved scheduled job handlers

Most applications should reference a concrete provider package such as `EventStoreCore.Hangfire` or `EventStoreCore.Quartz` rather than using this package directly.

## Contract

- Scheduling runs through the EventStoreCore subscription pipeline and is therefore at-least-once.
- Reprocessing the same `EventId` with the same `ScheduleKey` is treated as a no-op by supported providers.
- A later event using the same `ScheduleKey` replaces the previously scheduled work.
- Cancel for a missing or already-removed schedule is treated as a no-op.
- Scheduled job handlers must still be idempotent and re-check current state before acting.

See the repository README for end-to-end provider examples:

- [Repository README](https://github.com/EdvardJinder/eventstore/blob/main/README.md)
