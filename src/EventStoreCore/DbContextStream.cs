using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

/// <summary>
/// EF Core-backed stream implementation.
/// </summary>
/// <param name="dbStream">The persisted stream record.</param>
public class DbContextStream(DbStream dbStream) : IStream
{
    /// <summary>
    /// Creates a stream wrapper for the provided stream record.
    /// </summary>
    /// <param name="dbStream">The persisted stream.</param>
    /// <param name="db">The DbContext that owns the stream.</param>
    public DbContextStream(DbStream dbStream, DbContext db) : this(dbStream)
    {
        _ = db;
    }

    /// <summary>
    /// The tenant identifier for multi-tenant scenarios.
    /// </summary>
    public Guid TenantId => dbStream.TenantId;

    /// <inheritdoc />
    public Guid Id => dbStream.Id;

    /// <inheritdoc />
    public long Version => dbStream.CurrentVersion;

    /// <inheritdoc />
    public IReadOnlyList<IEvent> Events => dbStream.Events
        .OrderBy(e => e.Version)
        .Select(e =>
        {
            var type = Type.GetType(e.Type!)!;
            var eventType = typeof(Event<>).MakeGenericType(type);
            return (IEvent)Activator.CreateInstance(eventType, e)!;
        })
        .ToList();

    /// <inheritdoc />
    public void Append(params IEnumerable<object> events)
    {
        foreach (var @event in events)
        {
            var dbEvent = new DbEvent
            {
                TenantId = dbStream.TenantId,
                StreamId = dbStream.Id,
                Version = ++dbStream.CurrentVersion,
                Type = @event.GetType().AssemblyQualifiedName!,
                Data = System.Text.Json.JsonSerializer.Serialize(@event),
                Timestamp = DateTimeOffset.UtcNow,
                EventId = Guid.NewGuid()
            };

            dbStream.Events.Add(dbEvent);
        }

        dbStream.UpdatedTimestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// EF Core-backed typed stream implementation.
/// </summary>
/// <typeparam name="T">The state type rebuilt from the stream.</typeparam>
/// <param name="dbStream">The persisted stream record.</param>
public class DbContextStream<T>(DbStream dbStream) : DbContextStream(dbStream), IStream<T> where T : IState, new()
{
    /// <summary>
    /// Creates a typed stream wrapper for the provided stream record.
    /// </summary>
    /// <param name="dbStream">The persisted stream.</param>
    /// <param name="db">The DbContext that owns the stream.</param>
    public DbContextStream(DbStream dbStream, DbContext db) : this(dbStream)
    {
        _ = db;
    }

    /// <inheritdoc />
    public T State
    {
        get
        {
            var state = new T();
            foreach (var @event in Events)
            {
                state.Apply(@event);
            }
            return state;
        }
    }
}

