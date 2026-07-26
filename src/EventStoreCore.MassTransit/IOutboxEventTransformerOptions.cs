using EventStoreCore.Abstractions;

namespace EventStoreCore.MassTransit;

/// <summary>
/// Configures entity-outbox event transformations for MassTransit.
/// </summary>
public interface IOutboxEventTransformerOptions
{
    /// <summary>
    /// Maps an incoming entity-outbox event type to an outgoing message type.
    /// </summary>
    /// <typeparam name="TIn">The incoming outbox event payload type.</typeparam>
    /// <typeparam name="TOut">The outgoing message type.</typeparam>
    /// <param name="transformer">Transformation function.</param>
    void AddEvent<TIn, TOut>(Func<IOutboxEvent<TIn>, TOut> transformer)
        where TIn : class;
}
