using EventStoreCore.Abstractions;

namespace EventStoreCore.MassTransit;

internal sealed class OutboxEventTransformerOptions : IOutboxEventTransformerOptions
{
    internal Dictionary<Type, List<(Type Out, Func<IOutboxEvent, object?> Transform)>> Handlers { get; } = [];

    public void AddEvent<TIn, TOut>(Func<IOutboxEvent<TIn>, TOut> transformer)
        where TIn : class
    {
        ArgumentNullException.ThrowIfNull(transformer);

        if (!Handlers.TryGetValue(typeof(TIn), out var handlers))
        {
            handlers = [];
            Handlers.Add(typeof(TIn), handlers);
        }

        handlers.Add((
            typeof(TOut),
            @event => @event is IOutboxEvent<TIn> typed ? transformer(typed) : null));
    }
}
