using System.Text.Json.Nodes;

namespace EventStoreCore;

/// <summary>
/// Configures aliases and upcasters for a logical event type.
/// </summary>
/// <typeparam name="TEvent">The current event payload type.</typeparam>
public interface IEventTypeBuilder<TEvent>
    where TEvent : class
{
    /// <summary>
    /// Adds an alternate logical name that uses the current event payload contract.
    /// </summary>
    /// <param name="eventTypeName">The alternate logical event type name.</param>
    /// <returns>The builder for chaining.</returns>
    IEventTypeBuilder<TEvent> AddAlias(string eventTypeName);

    /// <summary>
    /// Adds an upcaster from an older event payload type to the current event payload type.
    /// </summary>
    /// <typeparam name="TOldEvent">The older event payload type.</typeparam>
    /// <param name="fromEventTypeName">The logical event type name stored for the older payload.</param>
    /// <param name="upcaster">Maps the older payload to the current payload.</param>
    /// <returns>The builder for chaining.</returns>
    IEventTypeBuilder<TEvent> AddUpcaster<TOldEvent>(
        string fromEventTypeName,
        Func<TOldEvent, TEvent> upcaster)
        where TOldEvent : class;

    /// <summary>
    /// Adds an upcaster from an older JSON payload shape to the current event payload type.
    /// </summary>
    /// <param name="fromEventTypeName">The logical event type name stored for the older payload.</param>
    /// <param name="upcaster">Maps the older JSON payload to the current payload.</param>
    /// <returns>The builder for chaining.</returns>
    IEventTypeBuilder<TEvent> AddUpcaster(
        string fromEventTypeName,
        Func<JsonObject, TEvent> upcaster);

    /// <summary>
    /// Adds one deterministic step in the schema-version chain for this logical event type.
    /// </summary>
    /// <param name="fromSchemaVersion">The inclusive source schema version.</param>
    /// <param name="toSchemaVersion">The target schema version produced by the upcaster.</param>
    /// <param name="upcaster">Transforms the stored representation without transport-specific concepts.</param>
    /// <returns>The builder for chaining.</returns>
    IEventTypeBuilder<TEvent> AddUpcaster(
        int fromSchemaVersion,
        int toSchemaVersion,
        Func<string, string> upcaster);
}
