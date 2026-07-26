namespace EventStoreCore.Abstractions;

/// <summary>
/// Reads captured EF entity events and safely removes fully consumed rows.
/// </summary>
public interface IOutboxReader
{
    /// <summary>
    /// Reads an ordered batch after the supplied sequence.
    /// </summary>
    /// <param name="afterSequence">The exclusive lower sequence bound.</param>
    /// <param name="maxCount">The maximum number of events to return.</param>
    /// <param name="tenantId">An optional tenant filter.</param>
    /// <param name="ct">The cancellation token.</param>
    Task<IReadOnlyList<IOutboxEvent>> ReadAsync(
        long afterSequence,
        int maxCount = 100,
        Guid? tenantId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes rows up to the requested sequence without passing the slowest persisted subscription checkpoint.
    /// </summary>
    /// <param name="throughSequence">The inclusive requested upper sequence bound.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The number of deleted rows.</returns>
    Task<int> CleanupAsync(long throughSequence, CancellationToken ct = default);
}
