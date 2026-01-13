using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Persistence.EntityFrameworkCore;

public static class DbContextExtensions
{
    extension(DbContext dbContext)
    {
        public IEventStore Streams => dbContext.Streams();

        public DbSet<DbEvent> Events => dbContext.Events();

    }
    private static IEventStore Streams(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return new DbContextEventStore(dbContext);
    }

    private static DbSet<DbEvent> Events(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Set<DbEvent>();
    }
}
