using Azure.Messaging;
using IM.EventStore.Abstractions;

namespace IM.EventStore.CloudEvents;


public sealed class CloudEventTransformerOptions
{
    internal Dictionary<Type, Func<IEvent, CloudEvent>> _mappings = new();
    public void MapEvent<TEvent>(Func<IEvent<TEvent>, CloudEvent> transform)
        where TEvent : class
    {
        _mappings[typeof(TEvent)] = (ievent) => transform((IEvent<TEvent>)ievent);
    }

    public void MapEvent<TEvent>(string type, string source, Func<IEvent<TEvent>, string> subject)
       where TEvent : class
    {
        _mappings[typeof(TEvent)] = (ievent) =>
        {
            return new CloudEvent(
                source: source,
                type: type,
                jsonSerializableData: ((IEvent<TEvent>)ievent).Data,
                dataSerializationType: ievent.EventType
                )
            {
                Time = ievent.Timestamp,
                Subject = subject((IEvent<TEvent>)ievent),
                ExtensionAttributes =
                {
               ["tenantid"] = ievent.TenantId.ToString()
                }
            };
        };
    }
}
