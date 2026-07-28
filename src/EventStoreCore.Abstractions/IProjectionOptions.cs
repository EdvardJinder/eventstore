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
    /// Includes events with the specified logical event type.
    /// Multiple values in one category are combined with OR; categories are combined with AND.
    /// </summary>
    /// <param name="logicalEventType">The non-empty logical event type name.</param>
    /// <exception cref="NotSupportedException">
    /// The projection options implementation does not support persisted event filters.
    /// </exception>
    void IncludeLogicalEventType(string logicalEventType) =>
        throw new NotSupportedException(
            "This projection options implementation does not support persisted event filters.");

    /// <summary>
    /// Includes events from the specified logical stream type.
    /// Multiple values in one category are combined with OR; categories are combined with AND.
    /// </summary>
    /// <param name="streamType">The stream type, including an empty string for the default stream type.</param>
    /// <exception cref="NotSupportedException">
    /// The projection options implementation does not support persisted event filters.
    /// </exception>
    void IncludeStreamType(string streamType) =>
        throw new NotSupportedException(
            "This projection options implementation does not support persisted event filters.");

    /// <summary>
    /// Includes events from the specified stream identifier.
    /// Multiple values in one category are combined with OR; categories are combined with AND.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <exception cref="NotSupportedException">
    /// The projection options implementation does not support persisted event filters.
    /// </exception>
    void IncludeStream(Guid streamId) =>
        throw new NotSupportedException(
            "This projection options implementation does not support persisted event filters.");

    /// <summary>
    /// Includes events for the specified tenant.
    /// Multiple values in one category are combined with OR; categories are combined with AND.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <exception cref="NotSupportedException">
    /// The projection options implementation does not support persisted event filters.
    /// </exception>
    void IncludeTenant(Guid tenantId) =>
        throw new NotSupportedException(
            "This projection options implementation does not support persisted event filters.");

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
}
