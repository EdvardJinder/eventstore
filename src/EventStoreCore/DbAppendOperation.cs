namespace EventStoreCore;

internal sealed class DbAppendOperation
{
    public Guid IdempotencyKey { get; set; }

    public string RequestHash { get; set; } = string.Empty;

    public Guid StreamId { get; set; }

    public string StreamType { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public long PreviousVersion { get; set; }

    public long CurrentVersion { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
