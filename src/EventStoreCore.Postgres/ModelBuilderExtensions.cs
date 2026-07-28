using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Postgres;

/// <summary>
/// Postgres-specific EF Core model configuration for the event store.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures the event store schema using Postgres column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.RelationalModelBuilderExtensions
            .ConfigureEventStoreRelationalModel(
                modelBuilder,
                new global::EventStoreCore.RelationalProviderModelOptions("jsonb"));
    }

    /// <summary>
    /// Configures only the standalone EF entity-outbox schema using Postgres column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEntityOutbox(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.RelationalModelBuilderExtensions
            .ConfigureEntityOutboxRelationalModel(
                modelBuilder,
                new global::EventStoreCore.RelationalProviderModelOptions("jsonb"));
    }
}
