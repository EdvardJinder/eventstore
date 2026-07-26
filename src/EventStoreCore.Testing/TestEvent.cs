using EventStoreCore.Abstractions;

namespace EventStoreCore.Testing;

/// <summary>
/// Provides a deterministic, strongly typed event envelope for application tests.
/// </summary>
/// <typeparam name="T">The event payload type.</typeparam>
public sealed class TestEvent<T> : IEvent<T>
    where T : class
{
    /// <summary>
    /// Creates a test event with explicit identity, ordering, and metadata values.
    /// </summary>
    /// <param name="data">The event payload.</param>
    /// <param name="id">The event identifier. The default is <see cref="Guid.Empty"/>.</param>
    /// <param name="streamId">
    /// The stream identifier. When omitted, the value from <paramref name="metadata"/> is used,
    /// followed by <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="tenantId">
    /// The tenant identifier. When omitted, the value from <paramref name="metadata"/> is used,
    /// followed by <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="version">
    /// The stream version. When omitted, the value from <paramref name="metadata"/> is used,
    /// followed by <c>1</c>.
    /// </param>
    /// <param name="sequence">
    /// The global sequence. When omitted, the value from <paramref name="metadata"/> is used,
    /// followed by <c>1</c>.
    /// </param>
    /// <param name="timestamp">The event timestamp. The default is <see cref="DateTimeOffset.UnixEpoch"/>.</param>
    /// <param name="typeName">
    /// The logical event type name. When omitted, the value from <paramref name="metadata"/> is used,
    /// followed by the payload type name.
    /// </param>
    /// <param name="streamType">
    /// The logical stream type. When omitted, the value from <paramref name="metadata"/> is used,
    /// followed by the empty string.
    /// </param>
    /// <param name="metadata">
    /// Optional immutable metadata exposed by the event. When identity or ordering arguments are omitted, their
    /// values are initialized from this metadata where available.
    /// </param>
    public TestEvent(
        T data,
        Guid? id = null,
        Guid? streamId = null,
        Guid? tenantId = null,
        long? version = null,
        long? sequence = null,
        DateTimeOffset? timestamp = null,
        string? typeName = null,
        string? streamType = null,
        EventMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        Data = data;
        Id = id ?? Guid.Empty;
        StreamId = streamId ?? metadata?.StreamId ?? Guid.Empty;
        TenantId = tenantId ?? metadata?.TenantId ?? Guid.Empty;
        Version = version ?? metadata?.StreamVersion ?? 1;
        Sequence = sequence ?? metadata?.GlobalSequence ?? 1;
        Timestamp = timestamp ?? DateTimeOffset.UnixEpoch;
        TypeName = ResolveTypeName(typeName, metadata);
        StreamType = streamType ?? metadata?.StreamType ?? string.Empty;
        Metadata = new EventMetadata(
            metadata?.CorrelationId,
            metadata?.CausationId,
            metadata?.Actor,
            metadata?.Headers,
            metadata?.SchemaVersion ?? 1,
            TypeName,
            StreamType,
            TenantId,
            StreamId,
            Version,
            Sequence);
    }

    /// <summary>
    /// Gets the event identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the event version within its stream.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// Gets the strongly typed event payload.
    /// </summary>
    public T Data { get; }

    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// Gets the UTC timestamp assigned to the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// Gets the CLR type of the event payload.
    /// </summary>
    public Type EventType => typeof(T);

    /// <summary>
    /// Gets the logical event type name.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the logical stream type.
    /// </summary>
    public string StreamType { get; }

    /// <summary>
    /// Gets the global event ordering sequence.
    /// </summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets immutable event metadata aligned with this envelope.
    /// </summary>
    public EventMetadata Metadata { get; }

    object IEvent.Data => Data;

    private static string ResolveTypeName(string? typeName, EventMetadata? metadata)
    {
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        if (!string.IsNullOrWhiteSpace(metadata?.EventType))
        {
            return metadata.EventType;
        }

        return typeof(T).Name;
    }

}
