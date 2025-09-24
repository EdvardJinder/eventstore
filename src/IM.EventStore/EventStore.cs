using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IM.EventStore;

internal sealed class EventStore(DbContext db) : IEventStore
{
    public async Task<IReadOnlyStream> FetchForReadingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) throw new InvalidOperationException($"Stream {streamId} not found");
        return new Stream(stream, db);
    }

    public async Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new Stream(stream, db);
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
        var stream = new Stream(dbStream, db);
        stream.Append(events);
        return stream;
    }
}

