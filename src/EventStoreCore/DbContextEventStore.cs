using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IEventStore" />.
/// </summary>
/// <param name="db">The DbContext used for persistence.</param>
public sealed class DbContextEventStore(DbContext db) : IEventStore
{
    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, Guid.Empty, cancellationToken);

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
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var stream = await db.Set<DbStream>()
         .AsNoTracking()
         .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
         .Include(x => x.Events)
         .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db);
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, Guid.Empty, version, cancellationToken);

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
        return new DbContextStream(stream);
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(streamType, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events.Where(x => x.Version <= version))
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream);
    }

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(Guid streamId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(string.Empty, streamId, Guid.Empty, cancellationToken);

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
    public IStream StartStream(string streamType, Guid streamId, params IEnumerable<object> events)
        => StartStream(streamType, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream StartStream(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events)
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
        var stream = new DbContextStream(dbStream, db);

        stream.Append(events);
        return stream;
    }

    /// <inheritdoc />
    public IStream<T> StartStream<T>(Guid streamId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(string.Empty, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(string streamType, Guid streamId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(streamType, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events) where T : IState, new()
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
        var stream = new DbContextStream<T>(dbStream, db);

        stream.Append(events);
        return stream;
    }
}

