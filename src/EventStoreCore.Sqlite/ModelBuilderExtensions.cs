using EventStoreCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventStoreCore.Sqlite;

/// <summary>
/// SQLite-specific EF Core model configuration for the event store.
/// </summary>
public static class ModelBuilderExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter =
        new(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableUtcTicksConverter =
        new(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

    /// <summary>
    /// Configures the event store schema using SQLite column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);

        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("TEXT");
            entity.Property(e => e.Headers)
                .HasColumnType("TEXT");
            entity.Property(e => e.Timestamp)
                .HasConversion(UtcTicksConverter);
        });

        modelBuilder.Entity<DbSnapshot>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("TEXT");
            entity.Property(e => e.Timestamp)
                .HasConversion(UtcTicksConverter);
        });

        modelBuilder.Entity<DbStream>(entity =>
        {
            entity.Property(e => e.CreatedTimestamp)
                .HasConversion(UtcTicksConverter);
            entity.Property(e => e.UpdatedTimestamp)
                .HasConversion(UtcTicksConverter);
        });

        modelBuilder.Entity<DbSubscription>(entity =>
        {
            entity.Property(e => e.LastAttemptAt)
                .HasConversion(NullableUtcTicksConverter);
            entity.Property(e => e.NextAttemptAt)
                .HasConversion(NullableUtcTicksConverter);
        });

        modelBuilder.Entity<DbProjectionStatus>(entity =>
        {
            entity.Property(e => e.LastProcessedAt)
                .HasConversion(NullableUtcTicksConverter);
            entity.Property(e => e.RebuildStartedAt)
                .HasConversion(NullableUtcTicksConverter);
            entity.Property(e => e.RebuildCompletedAt)
                .HasConversion(NullableUtcTicksConverter);
        });
    }

    /// <summary>
    /// Configures only the standalone EF entity-outbox schema using SQLite
    /// column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEntityOutbox(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);

        modelBuilder.Entity<DbOutboxMessage>(entity =>
        {
            entity.Property(message => message.Data)
                .HasColumnType("TEXT");
            entity.Property(message => message.SourceEntityKey)
                .HasColumnType("TEXT");
            entity.Property(message => message.Timestamp)
                .HasConversion(UtcTicksConverter);
        });

        modelBuilder.Entity<DbOutboxSubscription>(entity =>
        {
            entity.Property(subscription => subscription.LastAttemptAt)
                .HasConversion(NullableUtcTicksConverter);
            entity.Property(subscription => subscription.NextAttemptAt)
                .HasConversion(NullableUtcTicksConverter);
        });
    }
}
