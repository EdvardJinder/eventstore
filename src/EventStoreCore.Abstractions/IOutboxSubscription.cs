namespace EventStoreCore.Abstractions;

/// <summary>
/// Handles events read from the EF entity outbox.
/// </summary>
public interface IOutboxSubscription
{
    /// <summary>
    /// Processes one outbox event. Implementations must be idempotent because delivery is at-least-once.
    /// </summary>
    /// <param name="event">The captured entity event.</param>
    /// <param name="ct">The cancellation token.</param>
    Task Handle(IOutboxEvent @event, CancellationToken ct);
}
