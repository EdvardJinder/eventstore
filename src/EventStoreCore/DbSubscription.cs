namespace EventStoreCore;

using EventStoreCore.Abstractions;

internal sealed class DbSubscription
{
    /// <summary>
    /// The subscription's assembly-qualified type name.
    /// </summary>
    public string SubscriptionAssemblyQualifiedName { get; set; } = null!;

    /// <summary>
    /// Whether this checkpoint is shared globally or belongs to a tenant.
    /// </summary>
    public CheckpointScope CheckpointScope { get; set; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant this checkpoint belongs to when <see cref="CheckpointScope" /> is <see cref="Abstractions.CheckpointScope.Tenant" />.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;

    /// <summary>
    /// The last processed event sequence number.
    /// </summary>
    public long Sequence { get; set; } = 0;

    /// <summary>
    /// The current lifecycle state of the subscription.
    /// </summary>
    public SubscriptionState State { get; set; } = SubscriptionState.Active;

    /// <summary>
    /// The last error observed while processing the current failed event.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The number of attempts made against the current failed event.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// When the current failed event was last attempted.
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// When the daemon should next retry the current failed event.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// The sequence number of the event that caused the current fault.
    /// </summary>
    public long? FailedEventSequence { get; set; }

    /// <summary>
    /// Converts this entity to an external status DTO.
    /// </summary>
    /// <param name="totalEvents">The total number of events in the store.</param>
    /// <param name="lastProcessedAt">When the last processed event occurred.</param>
    /// <param name="processedEvents">The number of events processed in this checkpoint scope.</param>
    public SubscriptionStatusDto ToDto(
        long? totalEvents,
        DateTimeOffset? lastProcessedAt,
        long? processedEvents = null)
    {
        var progress = totalEvents.HasValue && totalEvents.Value > 0
            ? Math.Round((double)(processedEvents ?? Sequence) / totalEvents.Value * 100, 2)
            : (double?)null;

        return new SubscriptionStatusDto(
            SubscriptionAssemblyQualifiedName,
            Sequence,
            State,
            totalEvents,
            progress,
            lastProcessedAt,
            LastError,
            AttemptCount,
            LastAttemptAt,
            NextAttemptAt,
            FailedEventSequence)
        {
            CheckpointScope = CheckpointScope,
            TenantId = CheckpointScope == Abstractions.CheckpointScope.Tenant ? TenantId : null
        };
    }
}

