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

