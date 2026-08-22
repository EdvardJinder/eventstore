namespace EventStoreCore.Abstractions;

/// <summary>
/// Handles an event inline while the originating <c>SaveChanges</c> operation is in progress.
/// </summary>
/// <typeparam name="TEvent">The event payload type.</typeparam>
/// <remarks>
/// Implementations may mutate tracked state owned by the same persistence context. They must not
/// call <c>SaveChanges</c>, append streams, perform remote I/O, or execute other effects that cannot
/// be rolled back with the outer save.
/// </remarks>
public interface IInlineEventHandler<TEvent>
    where TEvent : class
{
    /// <summary>Handles one event before the originating save commits.</summary>
    /// <param name="event">The common event envelope.</param>
    /// <param name="ct">Cancellation token for the outer save.</param>
    Task Handle(IEventEnvelope<TEvent> @event, CancellationToken ct);
}
