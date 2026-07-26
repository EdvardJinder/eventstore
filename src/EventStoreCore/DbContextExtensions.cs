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

        /// <summary>
        /// Gets the explicit administrative stream lifecycle manager for the current context.
        /// </summary>
        public IStreamLifecycleManager StreamLifecycle => dbContext.StreamLifecycle();
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

    private static IStreamLifecycleManager StreamLifecycle(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return new DbContextStreamLifecycleManager(dbContext);
    }
}
