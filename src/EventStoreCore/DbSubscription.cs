namespace EventStoreCore;

/// <summary>
/// Represents persisted subscription progress.
/// </summary>
public sealed class DbSubscription
{
    /// <summary>
    /// The subscription's assembly-qualified type name.
    /// </summary>
    public string SubscriptionAssemblyQualifiedName { get; set; } = null!;

    /// <summary>
    /// The last processed event sequence number.
    /// </summary>
    public long Sequence { get; set; } = 0;
}

