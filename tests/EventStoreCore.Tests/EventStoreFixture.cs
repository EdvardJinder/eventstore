using EventStoreCore;
using EventStoreCore.Postgres;

using Microsoft.EntityFrameworkCore;
using static EventStoreCore.Tests.ProjectionTests;

namespace EventStoreCore.Tests;

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
    
    private EventStoreDbContext _context = null!;
    private DbContextOptions<EventStoreDbContext> _options = null!;
    
    public EventStoreDbContext Context
    {
        get => _context;
        private set => _context = value;
    }
    
    public EventStoreDbContext CreateNewContext()
    {
        return new EventStoreDbContext(_options);
    }

    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
           .UseNpgsql(_postgreSqlContainer.GetConnectionString())
           .Options;

        Context = new EventStoreDbContext(_options);

        // Ensure database is deleted and recreated to pick up schema changes
        await Context.Database.EnsureDeletedAsync();
        await Context.Database.EnsureCreatedAsync();
    }
    public new async ValueTask DisposeAsync()
    {
        await Context.Database.EnsureDeletedAsync();
        await Context.DisposeAsync();

        await base.DisposeAsync();
    }
}
