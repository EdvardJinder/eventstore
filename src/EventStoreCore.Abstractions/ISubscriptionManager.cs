namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides management operations for subscriptions including status, fault handling, and replay control.
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
    /// Gets the tenant-scoped status of a specific subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subscription status, or null if not found.</returns>
    Task<SubscriptionStatusDto?> GetStatusAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription status is not supported by this manager.");
    }

    /// <summary>
    /// Gets the status of all registered subscriptions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of all subscription statuses.</returns>
    Task<IReadOnlyList<SubscriptionStatusDto>> GetAllStatusesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant-scoped status of all registered subscriptions for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of subscription statuses for the tenant.</returns>
    Task<IReadOnlyList<SubscriptionStatusDto>> GetAllStatusesAsync(Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription status is not supported by this manager.");
    }

    /// <summary>
    /// Pauses processing of the specified subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>
    /// Pauses tenant-scoped processing of the specified subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription pause is not supported by this manager.");
    }

    /// <summary>
    /// Resumes processing of a paused subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResumeAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>
    /// Resumes tenant-scoped processing of a paused subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResumeAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription resume is not supported by this manager.");
    }

    /// <summary>
    /// Retries the failed event for a faulted or dead-lettered subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RetryFailedEventAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>
    /// Retries the failed tenant-scoped event for a faulted or dead-lettered subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RetryFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription retry is not supported by this manager.");
    }

    /// <summary>
    /// Skips the failed event and resumes processing from the next event.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SkipFailedEventAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed tenant-scoped event and resumes processing from the next event.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SkipFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription skip is not supported by this manager.");
    }

    /// <summary>
    /// Gets details about the failed event for a faulted or dead-lettered subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Details about the failed event, or null if the subscription is not faulted.</returns>
    Task<SubscriptionFailedEventDto?> GetFailedEventAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed tenant-scoped event for a faulted or dead-lettered subscription.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Details about the failed event, or null if the subscription is not faulted.</returns>
    Task<SubscriptionFailedEventDto?> GetFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription failed-event lookup is not supported by this manager.");
    }

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

    /// <summary>
    /// Replays a tenant-scoped subscription from a specific sequence or timestamp.
    /// </summary>
    /// <param name="subscriptionName">The subscription's assembly-qualified name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="startSequence">The sequence to start replaying from (inclusive).</param>
    /// <param name="fromTimestamp">Replay events starting from the first event at or after this timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReplayAsync(
        string subscriptionName,
        Guid tenantId,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("Tenant-scoped subscription replay is not supported by this manager.");
    }
}

/// <summary>
/// Represents the current status of a subscription.
/// </summary>
/// <param name="SubscriptionName">The unique subscription name.</param>
/// <param name="Position">The sequence number of the last processed event.</param>
/// <param name="State">The current lifecycle state of the subscription.</param>
/// <param name="TotalEvents">The total number of events in the event store.</param>
/// <param name="ProgressPercentage">The progress percentage based on current position.</param>
/// <param name="LastProcessedAt">When the last processed event occurred.</param>
/// <param name="LastError">The last error message if the subscription is faulted or dead-lettered.</param>
/// <param name="AttemptCount">The number of attempts made against the current failed event.</param>
/// <param name="LastAttemptAt">When the current failed event was last attempted.</param>
/// <param name="NextAttemptAt">When the daemon will next retry the failed event, if applicable.</param>
/// <param name="FailedEventSequence">The sequence number of the event that caused the fault.</param>
public sealed record SubscriptionStatusDto(
    string SubscriptionName,
    long Position,
    SubscriptionState State,
    long? TotalEvents,
    double? ProgressPercentage,
    DateTimeOffset? LastProcessedAt,
    string? LastError,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    long? FailedEventSequence
)
{
    /// <summary>
    /// Whether this status row is global or tenant-scoped.
    /// </summary>
    public CheckpointScope CheckpointScope { get; init; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant id for tenant-scoped status rows, or null for global status rows.
    /// </summary>
    public Guid? TenantId { get; init; }
}

/// <summary>
/// Represents the possible states of a subscription.
/// </summary>
public enum SubscriptionState
{
    /// <summary>
    /// The subscription is operating normally.
    /// </summary>
    Active = 0,

    /// <summary>
    /// The subscription has been manually paused.
    /// </summary>
    Paused = 1,

    /// <summary>
    /// The subscription encountered an error and will be retried.
    /// </summary>
    Faulted = 2,

    /// <summary>
    /// The subscription exceeded retry attempts and requires manual intervention.
    /// </summary>
    DeadLettered = 3
}

/// <summary>
/// Contains details about a failed subscription event for diagnostic purposes.
/// </summary>
/// <param name="EventId">The unique identifier of the event.</param>
/// <param name="StreamId">The stream the event belongs to.</param>
/// <param name="Version">The version of the event within its stream.</param>
/// <param name="Sequence">The global sequence number of the event.</param>
/// <param name="EventType">The type name of the event.</param>
/// <param name="Data">The serialized event data as JSON.</param>
/// <param name="Timestamp">When the event was created.</param>
/// <param name="SubscriptionError">The last processing error for the subscription.</param>
public sealed record SubscriptionFailedEventDto(
    Guid EventId,
    Guid StreamId,
    long Version,
    long Sequence,
    string EventType,
    string Data,
    DateTimeOffset Timestamp,
    string SubscriptionError
)
{
    /// <summary>
    /// Whether the failed event belongs to a global or tenant-scoped checkpoint.
    /// </summary>
    public CheckpointScope CheckpointScope { get; init; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant id for tenant-scoped failures, or null for global failures.
    /// </summary>
    public Guid? TenantId { get; init; }
}
