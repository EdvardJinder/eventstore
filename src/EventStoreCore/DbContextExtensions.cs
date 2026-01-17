using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

/// <summary>
/// EF Core extension helpers for event store access.
/// </summary>
public static class DbContextExtensions
{
    extension(DbContext dbContext)
    {
        /// <summary>
        /// Gets an <see cref="IEventStore" /> wrapper for the current context.
        /// </summary>
        public IEventStore Streams => dbContext.Streams();

        /// <summary>
        /// Gets the <see cref="DbEvent" /> DbSet for the current context.
        /// </summary>
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

