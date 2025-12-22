using EJ.EventStore.Abstractions;

namespace EJ.EventStore;

public class Event : IEvent
{
    public Event(DbEvent dbEvent)
    {
        Id = dbEvent.EventId;
        StreamId = dbEvent.StreamId;
        Version = dbEvent.Version;
        Timestamp = dbEvent.Timestamp;
        TenantId = dbEvent.TenantId;
        EventType = Type.GetType(dbEvent.Type!)!;
        Data = System.Text.Json.JsonSerializer.Deserialize(dbEvent.Data, EventType)!;
    }
    public Guid Id { get; }
    public Guid StreamId { get; }
    public long Version { get; }
    public object Data { get; }
    public DateTimeOffset Timestamp { get; }
    public Guid TenantId { get; }
    public Type EventType { get; }
}

public class Event<T> : Event, IEvent<T> where T : class
{
    public Event(DbEvent dbEvent) : base(dbEvent)
    {
        Data = System.Text.Json.JsonSerializer.Deserialize<T>(dbEvent.Data)!;
    }
    public new T Data { get; }
}
