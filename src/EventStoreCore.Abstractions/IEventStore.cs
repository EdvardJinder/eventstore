namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides access to event streams for reading and writing.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Reads one bounded page of events without materializing the entire stream.
    /// </summary>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="options">Range, direction, and page-size options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page when the stream exists; otherwise null.</returns>
    Task<StreamPage?> ReadPageAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously enumerates a bounded stream range without loading the entire range.
    /// </summary>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="options">Range, direction, and internal page-size options.</param>
    /// <param name="cancellationToken">Cancellation token observed between and during page queries.</param>
    /// <returns>Events in the requested ordering. A missing stream and an empty range both enumerate no events.</returns>
    IAsyncEnumerable<IEvent> ReadAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events using optimistic concurrency expectations.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="expectedVersion">The expected-version mode to enforce.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated read-only stream.</returns>
    Task<IReadOnlyStream> AppendAsync(
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events using optimistic concurrency expectations.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="expectedVersion">The expected-version mode to enforce.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated read-only stream.</returns>
    Task<IReadOnlyStream> AppendAsync(
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events using optimistic concurrency expectations.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="expectedVersion">The expected-version mode to enforce.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated read-only stream.</returns>
    Task<IReadOnlyStream> AppendAsync(
        string streamType,
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events using optimistic concurrency expectations.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="expectedVersion">The expected-version mode to enforce.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated read-only stream.</returns>
    Task<IReadOnlyStream> AppendAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for appending new events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream?> FetchForWritingAsync(Guid streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for appending new events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for appending new events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for appending new events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a typed stream for appending new events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for appending new events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for appending new events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for appending new events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The writable stream, or null when it does not exist.</returns>
    Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Creates a new stream and appends the supplied events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <returns>The created writable stream.</returns>
    IStream StartStream(Guid streamId, params IEnumerable<object> events);

    /// <summary>
    /// Creates a new stream and appends the supplied events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <returns>The created writable stream.</returns>
    IStream StartStream(Guid streamId, Guid tenantId, params IEnumerable<object> events);

    /// <summary>
    /// Creates a new stream and appends the supplied events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <returns>The created writable stream.</returns>
    IStream StartStream(string streamType, Guid streamId, params IEnumerable<object> events);

    /// <summary>
    /// Creates a new stream and appends the supplied events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <returns>The created writable stream.</returns>
    IStream StartStream(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events);

    /// <summary>
    /// Creates a new typed stream and appends the supplied events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The created writable stream.</returns>
    IStream<T> StartStream<T>(Guid streamId, params IEnumerable<object> events)
        where T : IState, new();

    /// <summary>
    /// Creates a new typed stream and appends the supplied events.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The created writable stream.</returns>
    IStream<T> StartStream<T>(Guid streamId, Guid tenantId, params IEnumerable<object> events)
        where T : IState, new();

    /// <summary>
    /// Creates a new typed stream and appends the supplied events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The created writable stream.</returns>
    IStream<T> StartStream<T>(string streamType, Guid streamId, params IEnumerable<object> events)
        where T : IState, new();

    /// <summary>
    /// Creates a new typed stream and appends the supplied events.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="events">Initial events to append to the stream.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The created writable stream.</returns>
    IStream<T> StartStream<T>(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events)
        where T : IState, new();

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, long version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, long version, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default)
        where T : IState, new();

    /// <summary>
    /// Fetches a typed stream for reading events without mutation.
    /// </summary>
    /// <param name="streamType">The stream type for distinguishing multiple streams with the same ID.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier for multi-tenant scenarios.</param>
    /// <param name="version">The maximum version to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
    /// <returns>The read-only stream, or null when it does not exist.</returns>
    Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default)
        where T : IState, new();
}


