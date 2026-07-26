namespace EventStoreCore.Abstractions;

/// <summary>
/// Handles events delivered by the subscription daemon.
/// </summary>
public interface ISubscription
{
    /// <summary>
    /// Processes a single event.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    Task Handle(IEvent @event, CancellationToken ct);
}

/// <summary>
/// Handles events with a strongly typed payload while retaining all event metadata.
/// </summary>
/// <typeparam name="TEvent">The event payload type.</typeparam>
public interface ISubscription<TEvent>
    where TEvent : class
{
    /// <summary>
    /// Processes a single strongly typed event.
    /// </summary>
    /// <param name="event">The event and its metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    Task Handle(IEvent<TEvent> @event, CancellationToken ct);
}

