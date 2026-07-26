using EventStoreCore.Abstractions;

namespace EventStoreCore.Testing;

/// <summary>
/// Provides a stream-scoped harness for exercising optimistic concurrency expectations
/// against an application-supplied event store.
/// </summary>
public sealed class OptimisticConcurrencyTestHarness
{
    private readonly IEventStore _eventStore;
    private readonly string _streamType;
    private readonly Guid _streamId;
    private readonly Guid _tenantId;

    /// <summary>
    /// Creates a harness for one logical stream.
    /// </summary>
    /// <param name="eventStore">The event store used by the application test.</param>
    /// <param name="streamType">The logical stream type. Use an empty string for the default stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier. The default value identifies the default tenant.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventStore"/> or <paramref name="streamType"/> is
    /// <see langword="null"/>.
    /// </exception>
    public OptimisticConcurrencyTestHarness(
        IEventStore eventStore,
        string streamType,
        Guid streamId,
        Guid tenantId = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(streamType);

        _eventStore = eventStore;
        _streamType = streamType;
        _streamId = streamId;
        _tenantId = tenantId;
    }

    /// <summary>
    /// Appends events using the supplied optimistic concurrency expectation.
    /// </summary>
    /// <param name="expectedVersion">The expected stream version to enforce.</param>
    /// <param name="events">The event payloads to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated read-only stream.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="events"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EventStoreConcurrencyException">
    /// Thrown when the supplied expectation does not match the current stream version.
    /// </exception>
    public Task<IReadOnlyStream> AppendAsync(
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        return _eventStore.AppendAsync(
            _streamType,
            _streamId,
            _tenantId,
            expectedVersion,
            events,
            cancellationToken);
    }

    /// <summary>
    /// Appends events and returns the expected optimistic concurrency exception.
    /// </summary>
    /// <param name="expectedVersion">The expected stream version that should be rejected.</param>
    /// <param name="events">The event payloads to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The concurrency exception raised by the event store.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="events"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the append succeeds instead of raising an
    /// <see cref="EventStoreConcurrencyException"/>.
    /// </exception>
    public async Task<EventStoreConcurrencyException> ExpectConflictAsync(
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        try
        {
            await AppendAsync(expectedVersion, events, cancellationToken).ConfigureAwait(false);
        }
        catch (EventStoreConcurrencyException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected an optimistic concurrency conflict for stream '{_streamType}/{_streamId}' " +
            $"in tenant '{_tenantId}' using expected version '{expectedVersion}', but the append succeeded.");
    }
}
