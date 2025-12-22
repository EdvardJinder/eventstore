using EJ.EventStore.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EJ.EventStore.Testing;

internal class TestDbContext : DbContext
{
    public DbSet<DbEvent> Events => Set<DbEvent>();
    public DbSet<DbStream> Stream => Set<DbStream>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("TestEventStore");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEventStore();
    }
}
