using EventStoreCore.Abstractions;

namespace EventStoreCore.MassTransit;

internal class EventTransformerOptions : IEventTransformerOptions
{
    public Dictionary<Type, List<(Type Out, Func<IEvent, object?> Transform)>> Handlers = [];
    public void AddEvent<TIn, TOut>(Func<IEvent<TIn>, TOut> transform) where TIn : class
    {
        Type type = typeof(TIn);
        Type outType = typeof(TOut);

        // Box the strongly-typed transformer into a Func<IEvent, object>.
        // If the incoming `IEvent` implements `IEvent<TEvent>` we can directly invoke the provided transform.
        // If it does not, we return null to indicate we cannot handle this runtime shape.
        Func<IEvent, object?> boxed = ev =>
        {
            if (ev is IEvent<TIn> typed)
            {
                return transform(typed)!;
            }
            // Can't safely adapt; return null to indicate no result.
            return null;
        };

        if (!Handlers.TryGetValue(type, out var handlerList))
        {
            handlerList = [];
            Handlers[type] = handlerList;
        }
        handlerList.Add((outType, boxed));
    }
}
