namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides status, recovery, and replay operations for entity-outbox subscriptions.
/// </summary>
public interface IOutboxSubscriptionManager
{
    /// <summary>Gets one global outbox-subscription status.</summary>
    Task<OutboxSubscriptionStatusDto?> GetStatusAsync(
        string subscriptionName,
        CancellationToken ct = default);

    /// <summary>Gets one tenant-scoped outbox-subscription status.</summary>
    Task<OutboxSubscriptionStatusDto?> GetStatusAsync(
        string subscriptionName,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>Gets all global outbox-subscription statuses.</summary>
    Task<IReadOnlyList<OutboxSubscriptionStatusDto>> GetAllStatusesAsync(
        CancellationToken ct = default);

    /// <summary>Gets all outbox-subscription statuses for one tenant.</summary>
    Task<IReadOnlyList<OutboxSubscriptionStatusDto>> GetAllStatusesAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>Pauses a global outbox subscription.</summary>
    Task PauseAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>Pauses a tenant-scoped outbox subscription.</summary>
    Task PauseAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default);

    /// <summary>Resumes a paused global outbox subscription.</summary>
    Task ResumeAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>Resumes a paused tenant-scoped outbox subscription.</summary>
    Task ResumeAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default);

    /// <summary>Retries the failed event for a global outbox subscription.</summary>
    Task RetryFailedEventAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>Retries the failed event for a tenant-scoped outbox subscription.</summary>
    Task RetryFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default);

    /// <summary>Skips the failed event for a global outbox subscription.</summary>
    Task SkipFailedEventAsync(string subscriptionName, CancellationToken ct = default);

    /// <summary>Skips the failed event for a tenant-scoped outbox subscription.</summary>
    Task SkipFailedEventAsync(string subscriptionName, Guid tenantId, CancellationToken ct = default);

    /// <summary>Gets the failed event for a global outbox subscription.</summary>
    Task<OutboxFailedEventDto?> GetFailedEventAsync(
        string subscriptionName,
        CancellationToken ct = default);

    /// <summary>Gets the failed event for a tenant-scoped outbox subscription.</summary>
    Task<OutboxFailedEventDto?> GetFailedEventAsync(
        string subscriptionName,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>Replays a global outbox subscription from a sequence or timestamp.</summary>
    Task ReplayAsync(
        string subscriptionName,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default);

    /// <summary>Replays a tenant-scoped outbox subscription from a sequence or timestamp.</summary>
    Task ReplayAsync(
        string subscriptionName,
        Guid tenantId,
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default);
}

/// <summary>
/// Represents the current status of an entity-outbox subscription.
/// </summary>
/// <param name="SubscriptionName">The stable subscription name.</param>
/// <param name="Position">The sequence of the last processed outbox event.</param>
/// <param name="State">The current lifecycle state.</param>
/// <param name="TotalEvents">The total number of outbox events in the checkpoint scope.</param>
/// <param name="ProgressPercentage">The checkpoint progress through the outbox scope.</param>
/// <param name="LastProcessedAt">When the last processed outbox event was captured.</param>
/// <param name="LastError">The latest processing error.</param>
/// <param name="AttemptCount">The number of attempts against the failed event.</param>
/// <param name="LastAttemptAt">When the failed event was last attempted.</param>
/// <param name="NextAttemptAt">When the daemon will next retry the event.</param>
/// <param name="FailedEventSequence">The failed outbox-event sequence.</param>
public sealed record OutboxSubscriptionStatusDto(
    string SubscriptionName,
    long Position,
    SubscriptionState State,
    long TotalEvents,
    double? ProgressPercentage,
    DateTimeOffset? LastProcessedAt,
    string? LastError,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    long? FailedEventSequence)
{
    /// <summary>Whether this status is global or tenant-scoped.</summary>
    public CheckpointScope CheckpointScope { get; init; } = CheckpointScope.Global;

    /// <summary>The checkpoint tenant, or null for a global checkpoint.</summary>
    public Guid? TenantId { get; init; }
}

/// <summary>
/// Describes an outbox event that faulted or dead-lettered a subscription.
/// </summary>
/// <param name="EventId">The stable outbox event identifier.</param>
/// <param name="Sequence">The outbox sequence.</param>
/// <param name="EventType">The logical event type.</param>
/// <param name="Data">The serialized event payload.</param>
/// <param name="Timestamp">When the event was captured.</param>
/// <param name="TenantId">The event tenant.</param>
/// <param name="SourceEntityType">The source entity CLR type name.</param>
/// <param name="SourceEntityKey">The serialized source entity key.</param>
/// <param name="ChangeKind">The source entity change kind.</param>
/// <param name="SubscriptionError">The latest subscription error.</param>
public sealed record OutboxFailedEventDto(
    Guid EventId,
    long Sequence,
    string EventType,
    string Data,
    DateTimeOffset Timestamp,
    Guid TenantId,
    string SourceEntityType,
    string SourceEntityKey,
    EntityChangeKind ChangeKind,
    string SubscriptionError)
{
    /// <summary>Whether the failed checkpoint is global or tenant-scoped.</summary>
    public CheckpointScope CheckpointScope { get; init; } = CheckpointScope.Global;
}
