using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

internal static class ModelBuilderExtensions
{
    internal static void ConfigureEventStoreModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbStream>(entity =>
        {
            entity.ToTable("Streams");

            entity.HasKey(e => new { e.Id, e.StreamType, e.TenantId });

            entity.Property(e => e.Id)
                    .IsRequired();

            entity.Property(e => e.StreamType)
                .IsRequired();

            entity.Property(e => e.CurrentVersion);

            entity.Property(e => e.CreatedTimestamp)
                .IsRequired();

            entity.Property(e => e.UpdatedTimestamp)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.HasIndex(e => e.TenantId);

            entity.HasIndex(e => new { e.TenantId, e.StreamType, e.CurrentVersion });

            entity.HasIndex(e => new { e.TenantId, e.StreamType, e.UpdatedTimestamp });

            entity.HasIndex(e => new { e.TenantId, e.StreamType, e.CreatedTimestamp });


            entity.HasMany(e => e.Events)
                .WithOne()
                .HasForeignKey(e => new { e.StreamId, e.StreamType, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.StreamType, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);

        });
        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(e => new { e.StreamId, e.StreamType, e.TenantId, e.Version });

            entity.HasAlternateKey(e => e.EventId);

            entity.Property(e => e.StreamId)
                    .IsRequired();

            entity.Property(e => e.StreamType)
                .IsRequired();

            entity.Property(e => e.Sequence)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Version)
                .IsRequired();

            entity.Property(e => e.Type)
                .IsRequired();

            entity.Property(e => e.TypeName)
                .IsRequired()
                .HasDefaultValue(string.Empty);

            entity.Property(e => e.Data)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.Property(e => e.CorrelationId);

            entity.Property(e => e.CausationId);

            entity.Property(e => e.Actor);

            entity.Property(e => e.Headers)
                .IsRequired()
                .HasDefaultValue("{}");

            entity.Property(e => e.SchemaVersion)
                .IsRequired()
                .HasDefaultValue(1);

            entity.HasIndex(e => e.TenantId);

            entity.HasIndex(e => new { e.TenantId, e.StreamId, e.StreamType });

            entity.HasIndex(e => new { e.TenantId, e.StreamType, e.Type });

            entity.HasIndex(e => new { e.TenantId, e.StreamType, e.Timestamp });
        });
        modelBuilder.Entity<DbSnapshot>(entity =>
        {
            entity.ToTable("Snapshots");

            entity.HasKey(e => new { e.StreamId, e.StreamType, e.TenantId, e.StateType });

            entity.Property(e => e.StreamId)
                .IsRequired();

            entity.Property(e => e.StreamType)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.StateType)
                .IsRequired();

            entity.Property(e => e.Version)
                .IsRequired();

            entity.Property(e => e.Data)
                .IsRequired();

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.Property(e => e.SchemaVersion)
                .IsRequired()
                .HasDefaultValue(1);

            entity.HasIndex(e => e.TenantId);

            entity.HasIndex(e => new { e.TenantId, e.StreamId, e.StreamType });
        });
        modelBuilder.Entity<DbSubscription>(entity =>
        {
            entity.ToTable("Subscriptions");

            entity.HasKey(e => new { e.SubscriptionAssemblyQualifiedName, e.CheckpointScope, e.TenantId });

            entity.Property(e => e.SubscriptionAssemblyQualifiedName)
                .IsRequired();

            entity.Property(e => e.CheckpointScope)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.Sequence)
                .IsRequired();

            entity.Property(e => e.State)
                .IsRequired();

            entity.Property(e => e.LastError);

            entity.Property(e => e.AttemptCount)
                .IsRequired();

            entity.Property(e => e.LastAttemptAt);

            entity.Property(e => e.NextAttemptAt);

            entity.Property(e => e.FailedEventSequence);

            entity.HasIndex(e => e.State);

            entity.HasIndex(e => new { e.CheckpointScope, e.TenantId, e.State });
        });

        modelBuilder.Entity<DbProjectionStatus>(entity =>
        {
            entity.ToTable("ProjectionStatuses");

            entity.HasKey(e => new { e.ProjectionName, e.CheckpointScope, e.TenantId });

            entity.Property(e => e.ProjectionName)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.CheckpointScope)
                .IsRequired();

            entity.Property(e => e.TenantId)
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

            entity.HasIndex(e => new { e.CheckpointScope, e.TenantId, e.State });
        });

        modelBuilder.Entity<DbSchedulerEventApplication>(entity =>
        {
            entity.ToTable("SchedulerEventApplications");

            entity.HasKey(e => new { e.ProviderName, e.RegistrationName, e.TenantId, e.EventId });

            entity.Property(e => e.ProviderName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.RegistrationName)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.EventId)
                .IsRequired();

            entity.Property(e => e.ClaimId)
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired();

            entity.Property(e => e.CompletedAtUtc);
        });
    }

}
