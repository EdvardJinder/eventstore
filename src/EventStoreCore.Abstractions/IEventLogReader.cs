namespace EventStoreCore.Abstractions;

/// <summary>
/// Reads the persisted event log across all streams in global sequence order.
/// </summary>
public interface IEventLogReader
{
    /// <summary>
    /// Reads one bounded page from a stable, commit-ordered view of the global event log.
    /// </summary>
    /// <param name="options">Sequence bounds, filters, and page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A page of events and the captured global high-water mark. With a supported provider and registered
    /// EventStoreCore context, no later commit can appear at or below that high-water mark.
    /// </returns>
    Task<EventLogPage> ReadPageAsync(
        EventLogReadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously enumerates a sequence-bounded view of the global event log.
    /// </summary>
    /// <param name="options">Sequence bounds, filters, and internal page size.</param>
    /// <param name="cancellationToken">Cancellation token observed between and during page queries.</param>
    /// <returns>Matching events in ascending global sequence order.</returns>
    IAsyncEnumerable<IEvent> ReadAsync(
        EventLogReadOptions options,
        CancellationToken cancellationToken = default);
}
