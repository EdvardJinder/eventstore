namespace EventStoreCore.Abstractions;

/// <summary>
/// Options for configuring which events a projection handles and how keys are derived.
/// </summary>
public interface IProjectionOptions
{
    /// <summary>
    /// Registers a handled event type.
    /// </summary>
    /// <typeparam name="T">The event payload type.</typeparam>
    void Handles<T>() where T : class;

    /// <summary>
    /// Marks the projection as handling all event types.
    /// </summary>
    void HandlesAll();

    /// <summary>
    /// Registers a handled event type with a custom snapshot key selector.
    /// </summary>
    /// <param name="keySelector">Selects a snapshot key for the event.</param>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = default) where TEvent : class;
}

