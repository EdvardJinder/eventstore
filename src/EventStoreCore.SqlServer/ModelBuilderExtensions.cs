using EventStoreCore;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.SqlServer;

/// <summary>
/// SQL Server-specific EF Core model configuration for the event store.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures the event store schema using SQL Server column types and commit-order lock metadata.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);
        ConfigureCommitOrderedSequences(modelBuilder, includesEvents: true, includesOutbox: false);

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
    /// Configures only the standalone EF entity-outbox schema using SQL Server column types and commit-order lock metadata.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void UseEntityOutbox(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);
        ConfigureCommitOrderedSequences(modelBuilder, includesEvents: false, includesOutbox: true);
        ConfigureOutboxProviderTypes(modelBuilder);
    }

    private static void ConfigureCommitOrderedSequences(
        ModelBuilder modelBuilder,
        bool includesEvents,
        bool includesOutbox)
    {
        modelBuilder.Model.SetAnnotation(
            SequenceCommitOrder.AcquireLockSqlAnnotation,
            """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'EventStoreCore.SequenceCommitOrder',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = -1;
            IF @result < 0
                THROW 51000, 'Could not acquire the EventStoreCore sequence commit-order lock.', 1;
            """);
        if (includesEvents)
        {
            modelBuilder.Model.SetAnnotation(
                SequenceCommitOrder.EventsInsertMarkerAnnotation,
                "INSERT INTO [Events]");
        }

        if (includesOutbox)
        {
            modelBuilder.Model.SetAnnotation(
                SequenceCommitOrder.OutboxInsertMarkerAnnotation,
                "INSERT INTO [OutboxMessages]");
        }
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
