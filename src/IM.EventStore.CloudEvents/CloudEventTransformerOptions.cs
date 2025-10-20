using Azure.Messaging;

namespace IM.EventStore.CloudEvents;

public sealed class CloudEventTransformerOptions
{
    internal Dictionary<Type, Func<IEvent, CloudEvent>> _mappings = new();
    public void MapEvent<TEvent>(Func<IEvent<TEvent>, CloudEvent> transform)
        where TEvent : class
    {
        _mappings[typeof(TEvent)] = (ievent) => transform((IEvent<TEvent>)ievent);
    }
}
