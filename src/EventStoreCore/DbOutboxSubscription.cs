using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Represents durable progress and retry state for an outbox subscription.
/// </summary>
/// <remarks>
/// This persistence type remains public for backwards compatibility.
/// </remarks>
public sealed class DbOutboxSubscription
{
    /// <summary>
    /// The subscription's assembly-qualified type name.
    /// </summary>
    public string SubscriptionAssemblyQualifiedName { get; set; } = null!;

    /// <summary>
    /// Whether progress is global or isolated per tenant.
    /// </summary>
    public CheckpointScope CheckpointScope { get; set; } = CheckpointScope.Global;

    /// <summary>
    /// The tenant identifier for tenant-scoped progress.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The last successfully processed outbox sequence.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The current lifecycle state.
    /// </summary>
    public SubscriptionState State { get; set; } = SubscriptionState.Active;

    /// <summary>
    /// The last processing error.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The number of attempts for the current failed message.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// When processing was last attempted.
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// When the failed message may next be retried.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// The sequence of the current failed message.
    /// </summary>
    public long? FailedEventSequence { get; set; }
}
