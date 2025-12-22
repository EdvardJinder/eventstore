
namespace IM.EventStore;

public sealed class DbStream
{
    public Guid TenantId { get; set; } = Guid.Empty;
    public Guid Id { get; set; }
    public long CurrentVersion { get; set; }
    public DateTimeOffset CreatedTimestamp { get; set; }
    public DateTimeOffset UpdatedTimestamp { get; set; }

    public ICollection<DbEvent> Events { get; set; } = new List<DbEvent>();
}
