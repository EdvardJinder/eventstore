using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EventStoreCore;

/// <summary>
/// EF Core-backed stream implementation.
/// </summary>
public class DbContextStream : IStream
{
    private readonly DbStream _dbStream;
    private readonly EventTypeRegistry? _registry;

    /// <summary>
    /// Creates a stream wrapper for the provided stream record.
    /// </summary>
    /// <param name="dbStream">The persisted stream record.</param>
    public DbContextStream(DbStream dbStream)
    {
        ArgumentNullException.ThrowIfNull(dbStream);
        _dbStream = dbStream;
    }

    /// <summary>
    /// Creates a stream wrapper for the provided stream record.
    /// </summary>
    /// <param name="dbStream">The persisted stream.</param>
    /// <param name="db">The DbContext that owns the stream.</param>
    public DbContextStream(DbStream dbStream, DbContext db) : this(dbStream)
    {
        ArgumentNullException.ThrowIfNull(db);

        var options = db.GetService<IDbContextOptions>();
        var appProvider = options.Extensions
            .OfType<CoreOptionsExtension>()
            .FirstOrDefault()
            ?.ApplicationServiceProvider;

        if (appProvider is null)
        {
            return;
        }

        _registry = appProvider.GetService(typeof(EventTypeRegistry)) as EventTypeRegistry
            ?? throw new InvalidOperationException(
                "EventTypeRegistry is not registered. Call services.AddEventStore() before using the event store.");
    }

    /// <summary>
    /// The tenant identifier for multi-tenant scenarios.
    /// </summary>
    public Guid TenantId => _dbStream.TenantId;

    /// <inheritdoc />
    public Guid Id => _dbStream.Id;

    /// <inheritdoc />
    public long Version => _dbStream.CurrentVersion;

    /// <inheritdoc />
    public IReadOnlyList<IEvent> Events => _dbStream.Events
        .OrderBy(e => e.Version)
        .Select(e => e.ToEvent(_registry))
        .ToList();

    /// <inheritdoc />
    public void Append(params IEnumerable<object> events)
    {
        foreach (var @event in events)
        {
            var eventType = @event.GetType();
            var typeName = _registry?.ResolveName(eventType) ?? EventTypeNameHelper.ToSnakeCase(eventType);
            var dbEvent = new DbEvent
            {
                TenantId = _dbStream.TenantId,
                StreamId = _dbStream.Id,
                StreamType = _dbStream.StreamType,
                Version = ++_dbStream.CurrentVersion,
                Type = eventType.AssemblyQualifiedName!,
                TypeName = typeName,
                Data = System.Text.Json.JsonSerializer.Serialize(@event),
                Timestamp = DateTimeOffset.UtcNow,
                EventId = Guid.NewGuid()
            };

            _dbStream.Events.Add(dbEvent);
        }

        _dbStream.UpdatedTimestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// EF Core-backed typed stream implementation.
/// </summary>
/// <typeparam name="T">The state type rebuilt from the stream.</typeparam>
public class DbContextStream<T> : DbContextStream, IStream<T> where T : IState, new()
{
    /// <summary>
    /// Creates a typed stream wrapper for the provided stream record.
    /// </summary>
    /// <param name="dbStream">The persisted stream record.</param>
    public DbContextStream(DbStream dbStream) : base(dbStream)
    {
    }

    /// <summary>
    /// Creates a typed stream wrapper for the provided stream record.
    /// </summary>
    /// <param name="dbStream">The persisted stream.</param>
    /// <param name="db">The DbContext that owns the stream.</param>
    public DbContextStream(DbStream dbStream, DbContext db) : base(dbStream, db)
    {
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

