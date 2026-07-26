using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace EventStoreCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IEventStore" />.
/// </summary>
/// <param name="db">The DbContext used for persistence.</param>
public sealed class DbContextEventStore(DbContext db) : IEventStore
{
    private readonly SnapshotRegistry? _snapshots = ResolveSnapshotRegistry(db);

    /// <inheritdoc />
    public Task<IReadOnlyStream> AppendAsync(
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
        => AppendAsync(string.Empty, streamId, Guid.Empty, expectedVersion, events, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream> AppendAsync(
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
        => AppendAsync(string.Empty, streamId, tenantId, expectedVersion, events, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream> AppendAsync(
        string streamType,
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
        => AppendAsync(streamType, streamId, Guid.Empty, expectedVersion, events, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream> AppendAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var eventList = events.ToArray();
        ValidateEventPayloads(eventList, nameof(events));
        var unrelatedChanges = CaptureUnrelatedChanges();
        var stream = await db.Set<DbStream>()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);

        stream = expectedVersion.Mode switch
        {
            ExpectedVersionMode.Any => stream ?? CreateStream(streamType, streamId, tenantId),
            ExpectedVersionMode.NoStream when stream is null => CreateStream(streamType, streamId, tenantId),
            ExpectedVersionMode.NoStream => throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                stream!.CurrentVersion,
                "Append expected no stream, but the stream already exists."),
            ExpectedVersionMode.StreamExists when stream is not null => stream,
            ExpectedVersionMode.StreamExists => throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                actualVersion: null,
                "Append expected an existing stream, but the stream was not found."),
            ExpectedVersionMode.Exact when stream is not null && stream.CurrentVersion == expectedVersion.Version => stream,
            ExpectedVersionMode.Exact => throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                stream?.CurrentVersion,
                $"Append expected stream version {expectedVersion.Version}, but observed {(stream is null ? "no stream" : stream.CurrentVersion.ToString())}."),
            _ => throw new InvalidOperationException($"Unsupported expected-version mode: {expectedVersion.Mode}")
        };

        new DbContextStream(stream, db).Append(eventList);
        SuppressUnrelatedChanges(unrelatedChanges);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new DbContextStream(stream, db);
        }
        catch (DbUpdateException ex) when (IsEventStoreWriteConflict(ex))
        {
            foreach (var entry in ex.Entries.Where(entry => entry.Entity is DbEvent or DbStream))
            {
                entry.State = EntityState.Detached;
            }

            var actualVersion = await GetActualVersionAsync(streamType, streamId, tenantId, cancellationToken);

            throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                actualVersion,
                "Append failed because another writer modified the stream concurrently.",
                ex);
        }
        finally
        {
            RestoreUnrelatedChanges(unrelatedChanges);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream(stream, db);
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var snapshot = _snapshots?.LoadSnapshot<T>(db, streamType, streamId, tenantId);
        var snapshotVersion = snapshot?.Version ?? 0;

        var stream = await db.Set<DbStream>()
         .AsNoTracking()
         .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
         .Include(x => x.Events.Where(e => e.Version > snapshotVersion))
         .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db, DeserializeSnapshot<T>(snapshot));
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, tenantId, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(streamType, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events.Where(x => x.Version <= version))
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        stream.CurrentVersion = Math.Min(version, stream.CurrentVersion);
        return new DbContextStream(stream);
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, tenantId, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(streamType, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var snapshot = _snapshots?.LoadSnapshot<T>(db, streamType, streamId, tenantId);
        if (snapshot?.Version > version)
        {
            snapshot = null;
        }

        var snapshotVersion = snapshot?.Version ?? 0;
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events.Where(x => x.Version > snapshotVersion && x.Version <= version))
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        stream.CurrentVersion = Math.Min(version, stream.CurrentVersion);
        return new DbContextStream<T>(stream, db, DeserializeSnapshot<T>(snapshot));
    }

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(Guid streamId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream(stream, db);
    }

    /// <inheritdoc />
    public Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForWritingAsync<T>(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForWritingAsync<T>(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForWritingAsync<T>(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var stream = await db.Set<DbStream>()
          .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
          .Include(x => x.Events)
          .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db);
    }

    /// <inheritdoc />
    public IStream StartStream(Guid streamId, params IEnumerable<object> events)
        => StartStream(string.Empty, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream StartStream(Guid streamId, Guid tenantId, params IEnumerable<object> events)
        => StartStream(string.Empty, streamId, tenantId, events);

    /// <inheritdoc />
    public IStream StartStream(string streamType, Guid streamId, params IEnumerable<object> events)
        => StartStream(streamType, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream StartStream(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var eventList = events.ToArray();
        ValidateEventPayloads(eventList, nameof(events));

        var dbStream = new DbStream
        {
            Id = streamId,
            StreamType = streamType,
            CurrentVersion = 0,
            CreatedTimestamp = DateTime.UtcNow,
            UpdatedTimestamp = DateTime.UtcNow,
            TenantId = tenantId
        };
        db.Add(dbStream);
        var stream = new DbContextStream(dbStream, db);

        stream.Append(eventList);
        return stream;
    }

    /// <inheritdoc />
    public IStream<T> StartStream<T>(Guid streamId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(string.Empty, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(Guid streamId, Guid tenantId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(string.Empty, streamId, tenantId, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(string streamType, Guid streamId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(streamType, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events) where T : IState, new()
    {
        ArgumentNullException.ThrowIfNull(events);
        var eventList = events.ToArray();
        ValidateEventPayloads(eventList, nameof(events));

        var dbStream = CreateStream(streamType, streamId, tenantId);
        var stream = new DbContextStream<T>(dbStream, db);

        stream.Append(eventList);
        return stream;
    }

    private DbStream CreateStream(string streamType, Guid streamId, Guid tenantId)
    {
        var dbStream = new DbStream
        {
            Id = streamId,
            StreamType = streamType,
            CurrentVersion = 0,
            CreatedTimestamp = DateTime.UtcNow,
            UpdatedTimestamp = DateTime.UtcNow,
            TenantId = tenantId
        };

        db.Add(dbStream);
        return dbStream;
    }

    private static bool IsEventStoreWriteConflict(DbUpdateException exception)
    {
        return exception.Entries.Any(entry => entry.Entity is DbEvent or DbStream)
            && IsUniqueConstraintViolation(exception);
    }

    private async Task<long?> GetActualVersionAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken)
    {
        return await db.Set<DbStream>()
            .AsNoTracking()
            .Where(stream => stream.Id == streamId && stream.StreamType == streamType && stream.TenantId == tenantId)
            .Select(stream => (long?)stream.CurrentVersion)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IReadOnlyList<SuppressedEntry> CaptureUnrelatedChanges()
    {
        return db.ChangeTracker.Entries()
            .Where(entry =>
                entry.Entity is not DbEvent and not DbStream &&
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => new SuppressedEntry(entry.Entity, entry.State))
            .ToArray();
    }

    private void SuppressUnrelatedChanges(IEnumerable<SuppressedEntry> entries)
    {
        foreach (var entry in entries)
        {
            db.Entry(entry.Entity).State = entry.State == EntityState.Added
                ? EntityState.Detached
                : EntityState.Unchanged;
        }
    }

    private void RestoreUnrelatedChanges(IEnumerable<SuppressedEntry> entries)
    {
        foreach (var entry in entries)
        {
            var trackedEntry = db.Entry(entry.Entity);
            trackedEntry.State = entry.State;
        }
    }

    private sealed record SuppressedEntry(object Entity, EntityState State);

    private static void ValidateEventPayloads(IEnumerable<object> events, string parameterName)
    {
        foreach (var @event in events)
        {
            ArgumentNullException.ThrowIfNull(@event, parameterName);
            var eventType = @event.GetType();
            if (eventType.IsValueType)
            {
                throw new ArgumentException(
                    $"Event payload type '{eventType.FullName}' is a value type. Event payloads must be reference types.",
                    parameterName);
            }
        }
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (GetStringProperty(current, "SqlState") == "23505")
            {
                return true;
            }

            if (GetIntProperty(current, "Number") is 2601 or 2627)
            {
                return true;
            }

            if (GetIntProperty(current, "SqliteErrorCode") == 19)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetStringProperty(Exception exception, string propertyName)
    {
        return exception.GetType().GetProperty(propertyName)?.GetValue(exception) as string;
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        return exception.GetType().GetProperty(propertyName)?.GetValue(exception) as int?;
    }

    private static EventStoreConcurrencyException CreateConcurrencyException(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        long? actualVersion,
        string message,
        Exception? innerException = null)
    {
        return new EventStoreConcurrencyException(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            actualVersion,
            message,
            innerException);
    }

    private static SnapshotRegistry? ResolveSnapshotRegistry(DbContext db)
    {
        try
        {
            var options = db.GetService<IDbContextOptions>();
            var appProvider = options.Extensions
                .OfType<CoreOptionsExtension>()
                .FirstOrDefault()
                ?.ApplicationServiceProvider;

            return appProvider?.GetService<SnapshotRegistry>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static T? DeserializeSnapshot<T>(DbSnapshot? snapshot)
        where T : IState, new()
        => snapshot is null ? default : JsonSerializer.Deserialize<T>(snapshot.Data);

}

