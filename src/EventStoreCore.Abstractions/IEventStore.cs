namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides access to event streams for reading and writing.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Fetches a stream for appending new events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId = default, string streamType = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a typed stream for appending new events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId = default, string streamType = "", CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Creates a new stream and appends the supplied events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <returns>The created writable stream.</returns>
    IStream StartStream(Guid streamId, Guid tenantId = default, string streamType = "", params IEnumerable<object> events);

    /// <summary>
    /// Creates a new typed stream and appends the supplied events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The created writable stream.</returns>
    IStream<T> StartStream<T>(Guid streamId, Guid tenantId = default, string streamType = "", params IEnumerable<object> events)
        where T : IState, new();

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId = default, string streamType = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, long version, Guid tenantId = default, string streamType = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId = default, string streamType = "", CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, long version, Guid tenantId = default, string streamType = "", CancellationToken cancellationToken = default)
        where T : IState, new();
}


