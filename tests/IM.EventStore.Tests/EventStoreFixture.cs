using IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using static IM.EventStore.Tests.ProjectionTests;

namespace IM.EventStore.Tests;

public class EventStoreFixture : PostgresFixture, IAsyncLifetime
{

    public class EventStoreDbContext : DbContext
    {
        public EventStoreDbContext(DbContextOptions options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.UseEventStore();

            modelBuilder.Entity<UserSnapshot>(e =>
            {
                e.HasKey(e => e.UserId);
            });

            modelBuilder.Entity<BookPageSummary>(e =>
            {
                e.HasKey(e => e.Id);
            });

        }
    }
    public EventStoreDbContext Context { get; private set; }
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
           .UseNpgsql(_postgreSqlContainer.GetConnectionString())
           .Options;

        Context = new EventStoreDbContext(options);

        await Context.Database.EnsureCreatedAsync();
    }
    public new async ValueTask DisposeAsync()
    {
        await Context.Database.EnsureDeletedAsync();
        await Context.DisposeAsync();

        await base.DisposeAsync();
    }
}
