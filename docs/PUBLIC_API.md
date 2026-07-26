# Public API policy

EventStoreCore is still pre-1.0. Public API changes may be made between beta releases, but they must be intentional, documented here, and covered by the public API contract tests.

## Supported extension points

The supported contracts are deliberately concentrated in these areas:

- `EventStoreCore.Abstractions`: events, typed and untyped streams, event stores, global event-log reads, projections, subscriptions, entity-outbox subscriptions, checkpoint scopes, optimistic-concurrency expectations, and their management DTOs.
- `EventStoreCore`: dependency-injection and EF Core builder interfaces and extension methods, projection and subscription options, snapshot configuration, event type registration, projection context helpers, and public operational exceptions.
- `EventStoreCore.Postgres` and `EventStoreCore.SqlServer`: provider-specific `ModelBuilder.UseEventStore` extensions.
- `EventStoreCore.CloudEvents`, `EventStoreCore.EventGrid`, and `EventStoreCore.MassTransit`: transport registration, transformation options, and transport subscription contracts.
- `EventStoreCore.Scheduling`, `EventStoreCore.Hangfire`, `EventStoreCore.Quartz`, and `EventStoreCore.TickerQ`: scheduler builders and provider-specific action registration.
- `EventStoreCore.Endpoints` and `EventStoreCore.SDK`: admin endpoint registration and its client contract.
- `EventStoreCore.Testing`: behavior-test base classes intended for application test projects.

EF persistence rows, EF-backed stream/store implementations, materialization helpers, daemon registrations, and other orchestration types are implementation details. Consumers should use the interfaces, builders, options, and provider extensions above rather than resolving or constructing implementation types.

## API review

Every shipped project generates XML documentation and enables the .NET SDK package validator. `eng/PublicApiBaseline.txt` records the documented symbols from every package, and CI compares each packed artifact with that reviewed baseline. The contract suite also locks down stream-interface and implementation-visibility boundaries. When adding, removing, or changing a public symbol:

1. Add a behavior or compile-time contract test for the new API.
2. Pack the solution, review the affected API, and run `eng/Validate-Packages.ps1 <package-directory> -UpdateBaseline` when the change is intentional.
3. Pack the solution and run the packed-package validation before publishing.

## Pre-1.0 compatibility notes

The following changes are intentional for the next beta:

- `IReadOnlyStream<T>` now inherits `IReadOnlyStream`, and `IStream<T>` now inherits both `IReadOnlyStream<T>` and `IStream`.
- All stream interfaces expose the complete persistence identity: `Id`, `StreamType`, and `TenantId`.
- Generic stream interfaces inherit common members instead of declaring duplicates.
- Projection versions are configured exclusively with `ProjectionVersionAttribute`; the default is version 1. The concrete projection-options implementation and its former `Version(int)` method are no longer public.
- The misspelled projection matching helper `IsHandeled` was renamed to `IsHandled` and made internal.
- EF persistence rows and EF-backed store/stream implementations are internal. Public event and stream interfaces remain the supported consumption boundary.
