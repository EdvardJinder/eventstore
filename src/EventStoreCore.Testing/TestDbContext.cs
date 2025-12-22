using EventStoreCore.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Testing;

internal class TestDbContext : DbContext
{
    private readonly string _dbName;

    public TestDbContext(string dbName)
    {
        _dbName = dbName;
    }

    public DbSet<DbEvent> Events => Set<DbEvent>();
    public DbSet<DbStream> Stream => Set<DbStream>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase(_dbName);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEventStore();
    }
}
