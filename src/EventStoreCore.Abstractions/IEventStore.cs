namespace EventStoreCore.Abstractions;

public interface IEventStore
{
    Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default);
    Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default)
        where T : IState, new();
    IStream StartStream(Guid streamId, Guid tenantId = default, params IEnumerable<object> events);
    IStream<T> StartStream<T>(Guid streamId, Guid tenantId = default, params IEnumerable<object> events)
        where T : IState, new();

    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default);
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId = default, CancellationToken cancellationToken = default)
        where T : IState, new();

}

