namespace EventStoreCore.Abstractions;

/// <summary>
/// Represents state that can be rebuilt from events.
/// </summary>
public interface IState
{
    /// <summary>
    /// Applies an event to mutate the state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    void Apply(IEvent @event);
}

