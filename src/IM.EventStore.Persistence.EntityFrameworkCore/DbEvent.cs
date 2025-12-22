
namespace IM.EventStore;

public sealed class DbEvent
{
    public Guid TenantId { get; set; } = Guid.Empty;
    public Guid StreamId { get; set; }
    public long Version { get; set; }
    public long Sequence { get; set; }
    public string Type { get; set; }
    public string Data { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid EventId { get; set; }
}
