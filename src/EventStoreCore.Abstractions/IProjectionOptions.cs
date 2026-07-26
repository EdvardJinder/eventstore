namespace EventStoreCore.Abstractions;

/// <summary>
/// Options for configuring which events a projection handles and how keys are derived.
/// </summary>
public interface IProjectionOptions
{
    /// <summary>
    /// Assigns a stable logical identity used by checkpoints, locks, status APIs, and logs.
    /// </summary>
    /// <param name="name">The non-empty logical projection name.</param>
    void Name(string name);

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

    /// <summary>
    /// Excludes a specific event type from processing.
    /// </summary>
    /// <typeparam name="T">The event payload type to ignore.</typeparam>
    void Ignores<T>() where T : class;

    /// <summary>
    /// Instructs the projection to skip events whose CLR type cannot be resolved
    /// instead of throwing an <see cref="System.Exception"/>.
    /// </summary>
    void IgnoreUnknown();

    /// <summary>
    /// Uses projection-owned shadow storage for rebuilds. The projection must implement the
    /// shadow lifecycle methods on <see cref="IProjection{TSnapshot}"/>. Shadow rebuilds are
    /// required for tenant-scoped checkpoints and keep the active read model available until
    /// the projection atomically activates the completed target.
    /// </summary>
    void UseShadowRebuilds()
    {
        throw new NotSupportedException("Shadow rebuild configuration is not supported by this projection provider.");
    }
}
