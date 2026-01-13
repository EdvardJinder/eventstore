using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Persistence.EntityFrameworkCore;

public static class ModelBuilderExtensions
{
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbStream>(entity =>
        {
            entity.ToTable("Streams");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                    .IsRequired();

            entity.Property(e => e.CurrentVersion);

            entity.Property(e => e.CreatedTimestamp)
                .IsRequired();

            entity.Property(e => e.UpdatedTimestamp)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.HasIndex(e => e.TenantId);

            entity.HasIndex(e => new { e.TenantId, e.Id })
                .IsUnique();

            entity.HasIndex(e => new { e.TenantId, e.CurrentVersion });

            entity.HasIndex(e => new { e.TenantId, e.UpdatedTimestamp });

            entity.HasIndex(e => new { e.TenantId, e.CreatedTimestamp });


            entity.HasMany(e => e.Events)
                .WithOne()
                .HasForeignKey(e => new { e.StreamId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);

        });
        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(e => new { e.StreamId, e.Version });

            entity.HasAlternateKey(e => e.EventId);

            entity.Property(e => e.StreamId)
                    .IsRequired();

            entity.Property(e => e.Sequence)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Version)
                .IsRequired();

            entity.Property(e => e.Type)
                .IsRequired();

            entity.Property(e => e.Data)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.HasIndex(e => e.TenantId);

            entity.HasIndex(e => new { e.TenantId, e.StreamId });

            entity.HasIndex(e => new { e.TenantId, e.Type });

            entity.HasIndex(e => new { e.TenantId, e.Timestamp });
        });
        modelBuilder.Entity<DbSubscription>(entity =>
        {
            entity.ToTable("Subscriptions");

            entity.HasKey(e => e.SubscriptionAssemblyQualifiedName);

            entity.Property(e => e.Sequence)
                    .IsRequired();
        });

        modelBuilder.Entity<DbProjectionStatus>(entity =>
        {
            entity.ToTable("ProjectionStatuses");

            entity.HasKey(e => e.ProjectionName);

            entity.Property(e => e.ProjectionName)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.Version)
                .IsRequired();

            entity.Property(e => e.State)
                .IsRequired();

            entity.Property(e => e.Position)
                .IsRequired();

            entity.Property(e => e.TotalEvents);

            entity.Property(e => e.LastProcessedAt);

            entity.Property(e => e.LastError);

            entity.Property(e => e.FailedEventSequence);

            entity.Property(e => e.RebuildStartedAt);

            entity.Property(e => e.RebuildCompletedAt);

            entity.HasIndex(e => e.State);
        });
    }

}
