using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace EventStoreCore;

/// <summary>
/// EF Core-backed stream implementation.
/// </summary>
public class DbContextStream : IStream
{
    private readonly DbStream _dbStream;
    private readonly DbContext? _db;
    private IReadOnlyList<IEvent>? _events;
    private readonly EventTypeRegistry? _registry;
    private readonly SnapshotRegistry? _snapshots;

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
        _db = db;

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
        _snapshots = appProvider.GetService<SnapshotRegistry>();
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
    public IReadOnlyList<IEvent> Events => _events ??= MaterializeEvents(_dbStream.Events);

    /// <inheritdoc />
    public void Append(params IEnumerable<object> events)
    {
        var previousVersion = _dbStream.CurrentVersion;
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
        _snapshots?.SaveSnapshots(_db!, _dbStream, previousVersion);
        _events = null;
    }

    protected IReadOnlyList<IEvent> MaterializeEvents(IEnumerable<DbEvent> events)
        => events
            .OrderBy(e => e.Version)
            .Select(e => e.ToEvent(_registry))
            .ToList();

}

/// <summary>
/// EF Core-backed typed stream implementation.
/// </summary>
/// <typeparam name="T">The state type rebuilt from the stream.</typeparam>
public class DbContextStream<T> : DbContextStream, IStream<T> where T : IState, new()
{
    private readonly T? _snapshotState;
    private readonly IReadOnlyList<IEvent>? _stateEvents;

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

    internal DbContextStream(DbStream dbStream, DbContext db, T? snapshotState) : base(dbStream, db)
    {
        _snapshotState = snapshotState;
        _stateEvents = MaterializeEvents(dbStream.Events);
    }

    /// <inheritdoc />
    public T State
    {
        get
        {
            var state = _snapshotState is null
                ? new T()
                : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(_snapshotState)) ?? new T();
            foreach (var @event in _stateEvents ?? Events)
            {
                state.Apply(@event);
            }
            return state;
        }
    }
}

