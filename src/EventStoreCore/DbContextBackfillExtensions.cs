using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EventStoreCore;

/// <summary>
/// Extension methods for backfilling event type names.
/// </summary>
public static class DbContextBackfillExtensions
{
    /// <summary>
    /// Populates missing <see cref="DbEvent.TypeName" /> values using the registered event registry
    /// and assembly-qualified name parsing.
    /// </summary>
    /// <param name="dbContext">The DbContext that owns the event store.</param>
    /// <param name="batchSize">The number of events to update per batch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of updated events.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when batch size is not positive.</exception>
    /// <exception cref="EventMaterializationException">Thrown when an event type name cannot be derived.</exception>
    public static Task<int> BackfillEventTypeNamesAsync(
        this DbContext dbContext,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        var registry = dbContext.GetService<EventTypeRegistry>();
        return BackfillEventTypeNamesAsync(dbContext, registry, batchSize, ct);
    }

    private static async Task<int> BackfillEventTypeNamesAsync(
        DbContext dbContext,
        EventTypeRegistry? registry,
        int batchSize,
        CancellationToken ct)
    {
        var updated = 0;

        while (true)
        {
            var events = await dbContext.Set<DbEvent>()
                .Where(e => string.IsNullOrWhiteSpace(e.TypeName))
                .OrderBy(e => e.Sequence)
                .Take(batchSize)
                .ToListAsync(ct);

            if (events.Count == 0)
            {
                break;
            }

            foreach (var dbEvent in events)
            {
                dbEvent.TypeName = ResolveTypeName(dbEvent, registry);
            }

            updated += events.Count;
            await dbContext.SaveChangesAsync(ct);
        }

        return updated;
    }

    private static string ResolveTypeName(DbEvent dbEvent, EventTypeRegistry? registry)
    {
        if (registry is not null && registry.TryGetName(dbEvent.Type, out var registeredName))
        {
            return registeredName;
        }

        var fallbackName = EventTypeNameHelper.GetDefaultNameFromAqn(dbEvent.Type);
        if (string.IsNullOrWhiteSpace(fallbackName))
        {
            throw new EventMaterializationException(
                $"Could not derive event type name from '{dbEvent.Type}'.",
                dbEvent);
        }

        return fallbackName;
    }
}
