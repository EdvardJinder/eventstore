using EventStoreCore.Abstractions;

namespace EventStoreCore.MassTransit;

public interface IEventTransformerOptions
{
    void AddEvent<TIn, TOut>(Func<IEvent<TIn>, TOut> transformer)
        where TIn : class;
}
