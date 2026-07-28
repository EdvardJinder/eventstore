using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Sqlite;

/// <summary>
/// SQLite-specific EF Core model configuration for the event store.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures the event store schema using SQLite column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.RelationalModelBuilderExtensions
            .ConfigureEventStoreRelationalModel(
                modelBuilder,
                new global::EventStoreCore.RelationalProviderModelOptions("TEXT")
                {
                    ConvertDateTimeOffsetsToUtcTicks = true
                });
    }

    /// <summary>
    /// Configures only the standalone EF entity-outbox schema using SQLite
    /// column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEntityOutbox(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.RelationalModelBuilderExtensions
            .ConfigureEntityOutboxRelationalModel(
                modelBuilder,
                new global::EventStoreCore.RelationalProviderModelOptions("TEXT")
                {
                    ConvertDateTimeOffsetsToUtcTicks = true
                });
    }
}
