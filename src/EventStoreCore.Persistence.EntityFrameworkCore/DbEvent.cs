
namespace EventStoreCore;

public sealed class DbEvent
{
    public Guid TenantId { get; set; } = Guid.Empty;
    public Guid StreamId { get; set; }
    public long Version { get; set; }
    public long Sequence { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }
    public Guid EventId { get; set; }
}
