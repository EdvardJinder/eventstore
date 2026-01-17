using EventStoreCore.Abstractions;

namespace EventStoreCore.MassTransit;

/// <summary>
/// Configures event transformations for MassTransit integration.
/// </summary>
public interface IEventTransformerOptions
{
    /// <summary>
    /// Maps an incoming event type to an outgoing message type.
    /// </summary>
    /// <typeparam name="TIn">The incoming event payload type.</typeparam>
    /// <typeparam name="TOut">The outgoing message type.</typeparam>
    /// <param name="transformer">Transformation function.</param>
    void AddEvent<TIn, TOut>(Func<IEvent<TIn>, TOut> transformer)
        where TIn : class;
}

