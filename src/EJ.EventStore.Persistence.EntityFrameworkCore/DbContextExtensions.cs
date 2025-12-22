using EJ.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EJ.EventStore.Persistence.EntityFrameworkCore;

public static class DbContextExtensions
{
    public static IEventStore Streams(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return new DbContextEventStore(dbContext);
    }

    public static DbSet<DbEvent> Events(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Set<DbEvent>();
    }
}
