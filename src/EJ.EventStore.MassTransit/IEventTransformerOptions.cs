using EJ.EventStore.Abstractions;

namespace EJ.EventStore.MassTransit;

public interface IEventTransformerOptions
{
    void AddEvent<TIn, TOut>(Func<IEvent<TIn>, TOut> transformer)
        where TIn : class;
}
