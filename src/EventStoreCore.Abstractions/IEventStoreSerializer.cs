namespace EventStoreCore.Abstractions;

/// <summary>
/// Serializes event and snapshot payloads for durable storage.
/// </summary>
/// <remarks>
/// Implementations must be deterministic for the same input and must remain capable of reading
/// historical payloads written with the configured format.
/// </remarks>
public interface IEventStoreSerializer
{
    /// <summary>
    /// Serializes a value using its declared payload type.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The declared payload type.</param>
    /// <returns>The serialized representation.</returns>
    string Serialize(object value, Type type);

    /// <summary>
    /// Deserializes a stored representation to the requested payload type.
    /// </summary>
    /// <param name="data">The stored representation.</param>
    /// <param name="type">The requested payload type.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the representation is null.</returns>
    object? Deserialize(string data, Type type);
}
