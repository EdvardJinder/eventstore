using EventStoreCore;
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
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);

        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("jsonb");

            entity.Property(e => e.Headers)
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<DbSnapshot>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("jsonb");
        });

    }

    /// <summary>
    /// Configures only the standalone EF entity-outbox schema using Postgres column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEntityOutbox(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);
        ConfigureOutboxProviderTypes(modelBuilder);
    }

    private static void ConfigureOutboxProviderTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbOutboxMessage>(entity =>
        {
            entity.Property(message => message.Data)
                .HasColumnType("jsonb");
            entity.Property(message => message.EntityKey)
                .HasColumnType("jsonb");
        });
    }
}

