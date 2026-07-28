using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventStoreCore;

/// <summary>
/// Provides the supported EF Core model-configuration boundary for relational
/// EventStoreCore provider packages.
/// </summary>
/// <remarks>
/// Application code should normally call the <c>UseEventStore</c> or
/// <c>UseEntityOutbox</c> extension supplied by its database provider package.
/// These methods are intended for authors of relational provider packages and
/// deliberately keep EventStoreCore persistence entities internal.
/// </remarks>
public static class RelationalModelBuilderExtensions
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
    /// Configures the EventStoreCore relational model and its serialized columns.
    /// </summary>
    /// <param name="modelBuilder">The EF Core model builder to configure.</param>
    /// <param name="options">The relational provider's storage capabilities.</param>
    public static void ConfigureEventStoreRelationalModel(
        this ModelBuilder modelBuilder,
        RelationalProviderModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(options);

        ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);

        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType(options.SerializedDataColumnType);
            entity.Property(e => e.Headers)
                .HasColumnType(options.SerializedDataColumnType);
        });

        modelBuilder.Entity<DbSnapshot>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType(options.SerializedDataColumnType);
        });

        if (options.ConvertDateTimeOffsetsToUtcTicks)
        {
            ConfigureEventStoreUtcTicks(modelBuilder);
        }
    }

    /// <summary>
    /// Configures the standalone EventStoreCore entity-outbox relational model
    /// and its serialized columns.
    /// </summary>
    /// <param name="modelBuilder">The EF Core model builder to configure.</param>
    /// <param name="options">The relational provider's storage capabilities.</param>
    public static void ConfigureEntityOutboxRelationalModel(
        this ModelBuilder modelBuilder,
        RelationalProviderModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(options);

        ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);

        modelBuilder.Entity<DbOutboxMessage>(entity =>
        {
            entity.Property(message => message.Data)
                .HasColumnType(options.SerializedDataColumnType);
            entity.Property(message => message.SourceEntityKey)
                .HasColumnType(options.SerializedDataColumnType);
        });

        if (options.ConvertDateTimeOffsetsToUtcTicks)
        {
            ConfigureEntityOutboxUtcTicks(modelBuilder);
        }
    }

    private static void ConfigureEventStoreUtcTicks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbStream>(entity =>
        {
            entity.Property(row => row.CreatedTimestamp).HasConversion(UtcTicksConverter);
            entity.Property(row => row.UpdatedTimestamp).HasConversion(UtcTicksConverter);
        });
        modelBuilder.Entity<DbEvent>(entity =>
            entity.Property(row => row.Timestamp).HasConversion(UtcTicksConverter));
        modelBuilder.Entity<DbSnapshot>(entity =>
            entity.Property(row => row.Timestamp).HasConversion(UtcTicksConverter));
        modelBuilder.Entity<DbSubscription>(entity =>
        {
            entity.Property(row => row.LastAttemptAt).HasConversion(NullableUtcTicksConverter);
            entity.Property(row => row.NextAttemptAt).HasConversion(NullableUtcTicksConverter);
        });
        modelBuilder.Entity<DbProjectionStatus>(entity =>
        {
            entity.Property(row => row.LastProcessedAt).HasConversion(NullableUtcTicksConverter);
            entity.Property(row => row.RebuildStartedAt).HasConversion(NullableUtcTicksConverter);
            entity.Property(row => row.RebuildCompletedAt).HasConversion(NullableUtcTicksConverter);
        });
    }

    private static void ConfigureEntityOutboxUtcTicks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbOutboxMessage>(entity =>
            entity.Property(row => row.Timestamp).HasConversion(UtcTicksConverter));
        modelBuilder.Entity<DbOutboxSubscription>(entity =>
        {
            entity.Property(row => row.LastAttemptAt).HasConversion(NullableUtcTicksConverter);
            entity.Property(row => row.NextAttemptAt).HasConversion(NullableUtcTicksConverter);
        });
    }
}
