namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides management operations for subscriptions including status and replay control.
/// </summary>
public interface ISubscriptionManager
{
    /// <summary>
    /// Gets the status of a specific subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subscription status, or null if not found.</returns>
    Task<SubscriptionStatusDto?> GetStatusAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of all registered subscriptions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of all subscription statuses.</returns>
    Task<IReadOnlyList<SubscriptionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default);

    /// <summary>
    /// Replays a subscription from a specific sequence or timestamp.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="startSequence">The sequence to start replaying from (inclusive).</param>
    /// <param name="fromTimestamp">Replay events starting from the first event at or after this timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReplayAsync(
        string subscriptionName,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default);
}

/// <summary>
/// Represents the current status of a subscription.
/// </summary>
/// <param name="SubscriptionName">The unique subscription name.</param>
/// <param name="Position">The sequence number of the last processed event.</param>
/// <param name="TotalEvents">The total number of events in the event store.</param>
/// <param name="ProgressPercentage">The progress percentage based on current position.</param>
/// <param name="LastProcessedAt">When the last processed event occurred.</param>
public sealed record SubscriptionStatusDto(
    string SubscriptionName,
    long Position,
    long? TotalEvents,
    double? ProgressPercentage,
    DateTimeOffset? LastProcessedAt
);
