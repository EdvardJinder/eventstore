namespace IM.EventStore;

public sealed class DbSubscription
{
    public Guid Id { get; set; }
    public string SubscriptionType { get; set; } = default!;
    public long CurrentSequence { get; set; }

    // For multi-node safety
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }

    // Concurrency (xmin in Postgres)
    public uint Version { get; set; }
}

