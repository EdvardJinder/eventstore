using IM.EventStore.Abstractions;
using IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;

internal sealed class DbContextEventStore(DbContext db) : IEventStore
{
    public async Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream(stream, db);
    }

    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var stream = await db.Set<DbStream>()
         .AsNoTracking()
         .Where(x => x.TenantId == tenantId)
         .Include(x => x.Events)
         .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db);
    }


    public async Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream(stream, db);
    }

    public async Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var stream = await db.Set<DbStream>()
          .Where(x => x.TenantId == tenantId)
          .Include(x => x.Events)
          .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db);
    }

    public IStream StartStream(Guid streamId, Guid tenantId = default, params IEnumerable<object> events)
    {
        var dbStream = new DbStream
        {
            Id = streamId,
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

    public IStream<T> StartStream<T>(Guid streamId, Guid tenantId = default, params IEnumerable<object> events) where T : IState, new()
    {
        var dbStream = new DbStream
        {
            Id = streamId,
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
