namespace EventStoreCore;

/// <summary>
/// Describes the small set of relational storage capabilities that
/// EventStoreCore provider packages may configure.
/// </summary>
public sealed class RelationalProviderModelOptions
{
    /// <summary>
    /// Creates relational provider model options.
    /// </summary>
    /// <param name="serializedDataColumnType">
    /// A provider-specific column type suitable for serialized payloads and
    /// JSON metadata.
    /// </param>
    public RelationalProviderModelOptions(string serializedDataColumnType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedDataColumnType);
        SerializedDataColumnType = serializedDataColumnType;
    }

    /// <summary>
    /// Gets the provider-specific column type for serialized payloads and JSON
    /// metadata.
    /// </summary>
    public string SerializedDataColumnType { get; }

    /// <summary>
    /// Gets or initializes whether <see cref="DateTimeOffset" /> values should
    /// be stored as UTC ticks for providers that cannot translate native
    /// <see cref="DateTimeOffset" /> ordering and range predicates.
    /// </summary>
    public bool ConvertDateTimeOffsetsToUtcTicks { get; init; }
}
