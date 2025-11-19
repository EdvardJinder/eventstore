using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

public static class EventStoreExtensions
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
                .UseIdentityColumn();

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
    }

    public static IServiceCollection AddEventStore<TDbContext>(
       this IServiceCollection services,
       Action<IServiceProvider, DbContextOptionsBuilder> optionsAction,
       Action<IEventStoreBuilder<TDbContext>>? configure = null
           )
           where TDbContext : DbContext
    {

        EventStoreBuilder<TDbContext> builder = new EventStoreBuilder<TDbContext>(services);

        configure?.Invoke(builder);

        services.AddDbContext<TDbContext>((sp, options) =>
        {
            optionsAction(sp, options);
            builder.ConfigureProjections(options);
        });

        return services;
    }

    extension<TDbContext>(TDbContext dbContext)
        where TDbContext : DbContext
    {
        public IEventStore Streams
        {
            get
            {
                ArgumentNullException.ThrowIfNull(dbContext);
                return new EventStore(dbContext);
            }
        }

        public DbSet<DbEvent> Events
        {
            get
            {
                ArgumentNullException.ThrowIfNull(dbContext);
                return dbContext.Set<DbEvent>();
            }
        }
    }
}
