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
        });
    }
}

