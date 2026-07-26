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
        /// Gets an <see cref="IEventLogReader" /> for reading events across all streams.
        /// </summary>
        public IEventLogReader EventLog => dbContext.EventLog();

        internal DbSet<DbEvent> Events => dbContext.Events();

    }
    private static IEventStore Streams(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return new DbContextEventStore(dbContext);
    }

    private static IEventLogReader EventLog(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return new DbContextEventLogReader(dbContext);
    }

    private static DbSet<DbEvent> Events(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Set<DbEvent>();
    }
}

