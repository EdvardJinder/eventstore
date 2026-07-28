# Relational provider extensibility

EventStoreCore supports a deliberately small boundary for relational EF Core
provider packages. Core owns its internal persistence rows and the complete
relational shape: tables, keys, relationships, indexes, generated sequences,
defaults, and value conversions. A provider package supplies only the database
column type used for serialized payloads and JSON metadata.

## Provider package extensions

A community provider can expose the same application-facing extensions as the
official packages without accessing any EventStoreCore persistence type:

```csharp
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.ExampleProvider;

public static class ModelBuilderExtensions
{
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureEventStoreRelationalModel(
            new RelationalProviderModelOptions("provider_json_type"));
    }

    public static void UseEntityOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureEntityOutboxRelationalModel(
            new RelationalProviderModelOptions("provider_json_type"));
    }
}
```

The supplied type must support required serialized event data, event headers,
snapshots, outbox payloads, and outbox source-entity key JSON. The two methods
are independent: `ConfigureEventStoreRelationalModel` configures stream storage,
while `ConfigureEntityOutboxRelationalModel` configures only the standalone
entity outbox.

Providers such as SQLite that cannot translate native `DateTimeOffset` ordering
and range predicates should opt into UTC-tick storage:

```csharp
var options = new RelationalProviderModelOptions("TEXT")
{
    ConvertDateTimeOffsetsToUtcTicks = true
};

modelBuilder.ConfigureEventStoreRelationalModel(options);
```

The conversion is applied by Core to its internal rows. UTC ticks preserve
chronological ordering but normalize persisted offsets to UTC.

## Compatibility requirements

A compatible provider must support:

- generated signed 64-bit integer primary keys for event and outbox sequences;
- unique indexes over GUID, string, and integer columns;
- composite keys and foreign keys;
- required `Guid`, `DateTimeOffset`, enum, nullable scalar, and string mappings;
- transactional `SaveChanges` behavior expected by EF Core;
- translation of equality, ordering, paging, and the filters used by event-log,
  projection, subscription, and outbox queries, either natively or through the
  supported UTC-tick conversion.

Provider packages must not copy, reflect over, or compile against internal
`Db*` persistence types. Internal schema details can change between pre-1.0
releases; the two relational configuration methods and their options are the
supported source and binary contract.

## Contract testing

Provider maintainers should test model creation, multiple generated sequences,
stream write/read, global event-log ordering, optimistic-concurrency conflicts,
and standalone outbox sequence generation against a real database engine.
Package tests should compile against packed `EventStoreCore` packages rather
than relying only on project references.

All current packages target `net10.0`. EF Core 10 and its official relational
providers expose only `net10.0` compile assets, so community providers for this
release should also target `net10.0`.
