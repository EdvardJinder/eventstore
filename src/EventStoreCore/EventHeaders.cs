using System.Text.Json;

namespace EventStoreCore;

internal static class EventHeaders
{
    internal static string Serialize(IReadOnlyDictionary<string, string> headers)
        => headers.Count == 0 ? "{}" : JsonSerializer.Serialize(headers);

    internal static IReadOnlyDictionary<string, string> Deserialize(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers))
        {
            return new Dictionary<string, string>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(headers)
            ?? new Dictionary<string, string>();
    }
}
