using EventStoreCore.Abstractions;
using System.Text.Json;

namespace EventStoreCore;

/// <summary>
/// Default event-store serializer backed by <see cref="JsonSerializer"/>.
/// </summary>
public sealed class SystemTextJsonEventStoreSerializer : IEventStoreSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Creates a serializer with the supplied JSON options.
    /// </summary>
    /// <param name="options">Options used for all event and snapshot payloads.</param>
    public SystemTextJsonEventStoreSerializer(JsonSerializerOptions? options = null)
    {
        _options = options is null ? new JsonSerializerOptions() : new JsonSerializerOptions(options);
    }

    /// <inheritdoc />
    public string Serialize(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Serialize(value, type, _options);
    }

    /// <inheritdoc />
    public object? Deserialize(string data, Type type)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Deserialize(data, type, _options);
    }
}
