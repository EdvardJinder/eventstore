using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public static class EventStoreExtensions
{
    extension(DbContext dbContext)
    {
        public IEventStore Events
        {
            get
            {
                ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));
                EventStore eventStore = new(dbContext);
                return eventStore;
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

            modelBuilder.Entity<DbSubscription>(entity =>
            {

                entity.ToTable("subscriptions");

                entity.HasKey(x => x.Id)
                    .HasName("pk_subscriptions");

                entity.HasIndex(x => x.SubscriptionType)
                    .IsUnique()
                    .HasDatabaseName("ux_subscriptions_subscription_type");

                entity.Property(x => x.Id)
                    .HasColumnName("id")
                    .IsRequired();

                entity.Property(x => x.SubscriptionType)
                    .HasColumnName("subscription_type");

                entity.Property(x => x.CurrentSequence)
                    .HasColumnName("current_sequence");

                entity.Property(x => x.LeaseOwner)
                    .HasColumnName("lease_owner");

                entity.Property(x => x.LeaseExpiresUtc)
                    .HasColumnName("lease_expires_utc");

                entity.Property(x => x.Version)
                    .HasColumnName("xmin")
                    .IsRowVersion();

            });
        }

    }

    extension(IServiceCollection services)
    {

        public void AddSubscription<TDbContext, TSubscription>(Action<IConfigureSubscription> configure)
            where TDbContext : DbContext
            where TSubscription : ISubscription
        {
            services.AddOptions<SubscriptionOptions>($"{typeof(TDbContext).Name}{typeof(TSubscription).Name}").Configure(configure);
            services.AddTransient(typeof(TSubscription));
            services.AddSingleton<SubscriptionWrapper<TDbContext, TSubscription>>();
            services.AddHostedService<SubscriptionWrapper<TDbContext, TSubscription>>(sp => sp.GetRequiredService<SubscriptionWrapper<TDbContext, TSubscription>>());
        }

        public void AddInlineProjection<TSnapshot, TProjection, TDbContext>()
            where TDbContext : DbContext
            where TProjection : IInlineProjection<TSnapshot>
            where TSnapshot : class, new()
        {
            services.AddTransient(typeof(TProjection));
            services.AddTransient(typeof(InlineProjection<TSnapshot, TProjection, TDbContext>));

            services.AddDbContext<TDbContext>((sp, options) =>
            {
                options.AddInterceptors(sp.GetRequiredService<InlineProjection<TSnapshot, TProjection, TDbContext>>());
            });

        }

        public void AddProjection<TSnapshot, TProjection, TDbContext>(Action<IConfigureSubscription> configure)
            where TDbContext : DbContext
            where TProjection : IProjection<TSnapshot>
            where TSnapshot : class, new()
        {
            services.AddTransient(typeof(TProjection));
            services.AddTransient(typeof(Projection<TSnapshot, TProjection, TDbContext>));
            services.AddSubscription<TDbContext, Projection<TSnapshot, TProjection, TDbContext>>(configure);
        }

    }
}
