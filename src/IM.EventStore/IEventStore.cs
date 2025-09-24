namespace IM.EventStore;

public interface IEventStore
{
    Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default);
    IStream StartStream(Guid streamId, Guid tenantId = default, params IEnumerable<object> events);

    Task<IReadOnlyStream> FetchForReadingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default);

}

