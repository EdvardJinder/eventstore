using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IM.EventStore;

public static class EventStoreExtensions
{
    //extension(DbContext dbContext)
    //{
    //    public IEventStore Streams
    //    {
    //        get
    //        {
    //            ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));
    //            EventStore eventStore = new(dbContext);
    //            return eventStore;
    //        }
    //    }

    //    public DbSet<DbEvent> Events
    //    {
    //        get
    //        {
    //            ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));
    //            return dbContext.Set<DbEvent>();
    //        }
    //    }
    //}

    
    public static IEventStoreBuilder AddEventStore<TDbContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> optionsAction
            )
            where TDbContext : DbContext
    {

        services.AddScoped<SubscriptionInterceptor>();

        services.AddDbContext<TDbContext>((sp, options) =>
        {
            optionsAction(sp, options);
            options.AddInterceptors(sp.GetRequiredService<SubscriptionInterceptor>());
        });

        return new EventStoreBuilder<TDbContext>(services);
    }
    public static IEventStore Streams(this DbContext dbContext)
    {
        if (dbContext == null) throw new ArgumentNullException(nameof(dbContext));
        EventStore eventStore = new(dbContext);
        return eventStore;
    }
    public static DbSet<DbEvent> Events(this DbContext dbContext)
    {
        if (dbContext == null) throw new ArgumentNullException(nameof(dbContext));
        return dbContext.Set<DbEvent>();
    }
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
            entity.ToTable("events");

            entity.HasKey(e => new { e.StreamId, e.Version });

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
    }

}
