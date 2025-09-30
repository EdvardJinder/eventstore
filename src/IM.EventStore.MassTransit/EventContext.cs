using System.Text.Json.Serialization;

namespace IM.EventStore.MassTransit;

public class EventContext<T> where T : class
{
    [JsonInclude] public T Data { get; private set; }
    [JsonInclude] public Guid EventId { get; private set; }
    [JsonInclude] public Guid StreamId { get; private set; }
    [JsonInclude] public long Version { get; private set; }
    [JsonInclude] public DateTimeOffset Timestamp { get; private set; }
    [JsonInclude] public Guid TenantId { get; private set; }

}