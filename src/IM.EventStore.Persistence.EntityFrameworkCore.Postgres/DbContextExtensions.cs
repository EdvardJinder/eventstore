using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IM.EventStore.Persistence.EntityFrameworkCore.Postgres;

public static class DbContextExtensions
{
    extension<TDbContext>(TDbContext dbContext)
        where TDbContext : DbContext
    {
        public IEventStore Streams
        {
            get
            {
                ArgumentNullException.ThrowIfNull(dbContext);
                return new DbContextEventStore(dbContext);
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
