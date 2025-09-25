using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public static class EventStoreExtensions
{
    extension(DbContext dbContext)
    {
        public IEventStore Streams
        {
            get
            {
                ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));
                EventStore eventStore = new(dbContext);
                return eventStore;
            }
        }

        public DbSet<DbEvent> Events
        {
            get
            {
                ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));
                return dbContext.Set<DbEvent>();
            }
        }
    }

    extension(ModelBuilder modelBuilder)
    {
        public void UseEventStore()
        {
            modelBuilder.Entity<DbStream>(entity =>
            {
                entity.ToTable("streams");

                entity.HasKey(e => e.Id)
                        .HasName("pk_streams");

                entity.Property(e => e.Id)
                        .HasColumnName("id")
                        .IsRequired();

                entity.Property(e => e.CurrentVersion)
                    .HasColumnName("version");

                entity.Property(e => e.CreatedTimestamp)
                    .HasColumnName("created_timestamp")
                    .IsRequired();

                entity.Property(e => e.UpdatedTimestamp)
                    .HasColumnName("updated_timestamp")
                    .IsRequired();

                entity.Property(e => e.TenantId)
                    .HasColumnName("tenant_id")
                    .IsRequired();

                entity.HasIndex(e => e.TenantId)
                    .HasDatabaseName("ix_streams_tenant_id");

                entity.HasIndex(e => new { e.TenantId, e.Id })
                    .IsUnique()
                    .HasDatabaseName("ux_streams_tenant_id_id");

                entity.HasIndex(e => new { e.TenantId, e.CurrentVersion })
                    .HasDatabaseName("ix_streams_tenant_id_version");

                entity.HasIndex(e => new { e.TenantId, e.UpdatedTimestamp })
                    .HasDatabaseName("ix_streams_tenant_id_updated_timestamp");

                entity.HasIndex(e => new { e.TenantId, e.CreatedTimestamp })
                    .HasDatabaseName("ix_streams_tenant_id_created_timestamp");


                entity.HasMany(e => e.Events)
                    .WithOne()
                    .HasForeignKey(e => new { e.StreamId, e.TenantId })
                    .HasPrincipalKey(e => new { e.Id, e.TenantId })
                    .OnDelete(DeleteBehavior.Cascade);

            });

            modelBuilder.Entity<DbEvent>(entity =>
            {
                entity.ToTable("events");

                entity.HasKey(e => new { e.StreamId, e.Version })
                        .HasName("pk_events");

                entity.Property(e => e.StreamId)
                        .HasColumnName("stream_id")
                        .IsRequired();

                entity.Property(e => e.Sequence)
                    .HasColumnName("sequence")
                    .UseIdentityColumn();

                entity.Property(e => e.Version)
                    .HasColumnName("version")
                    .IsRequired();

                entity.Property(e => e.Type)
                    .HasColumnName("type")
                    .IsRequired();

                entity.Property(e => e.Data)
                    .HasColumnName("data")
                    .IsRequired();

                entity.Property(e => e.TenantId)
                    .HasColumnName("tenant_id")
                    .IsRequired();

                entity.Property(e => e.Timestamp)
                    .HasColumnName("timestamp")
                    .IsRequired();

                entity.HasIndex(e => e.TenantId)
                    .HasDatabaseName("ix_events_tenant_id");

                entity.HasIndex(e => new { e.TenantId, e.StreamId })
                    .HasDatabaseName("ix_events_tenant_id_stream_id");

                entity.HasIndex(e => new { e.TenantId, e.Type })
                    .HasDatabaseName("ix_events_tenant_id_type");

                entity.HasIndex(e => new { e.TenantId, e.Timestamp })
                    .HasDatabaseName("ix_events_tenant_id_timestamp");
            });

        }

    }

    extension(IServiceCollection services)
    {
        public IEventStoreBuilder AddEventStore<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder> optionsAction
            )
            where TDbContext : DbContext
        {

            services.AddSingleton<SubscriptionInterceptor>();

            services.AddDbContext<TDbContext>((sp, options) =>
            {
                optionsAction(sp, options);
                options.AddInterceptors(sp.GetRequiredService<SubscriptionInterceptor>());
            });

            return new EventStoreBuilder<TDbContext>(services);
        }
    }
}

