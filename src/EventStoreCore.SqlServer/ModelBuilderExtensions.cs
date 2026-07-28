using EventStoreCore;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.SqlServer;

/// <summary>
/// SQL Server-specific EF Core model configuration for the event store.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures the event store schema using SQL Server column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);

        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.Headers)
                .HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<DbSnapshot>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("nvarchar(max)");
        });

    }

    /// <summary>
    /// Configures only the standalone EF entity-outbox schema using SQL Server column types.
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
                .HasColumnType("nvarchar(max)");
            entity.Property(message => message.SourceEntityKey)
                .HasColumnType("nvarchar(max)");
        });
    }
}
