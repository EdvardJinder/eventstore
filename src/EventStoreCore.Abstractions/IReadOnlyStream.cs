namespace EventStoreCore.Abstractions;

/// <summary>
/// Represents a stream of events that can be read.
/// </summary>
public interface IReadOnlyStream
{
    /// <summary>
    /// The stream identifier.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The logical stream type. The empty string identifies the default stream type.
    /// </summary>
    string StreamType { get; }

    /// <summary>
    /// The tenant identifier. <see cref="Guid.Empty"/> identifies the default tenant.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// The current stream version.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// The loaded events, ordered by version.
    /// Snapshot-backed typed reads may contain only the events after the snapshot used to rebuild the typed stream state.
    /// </summary>
    IReadOnlyList<IEvent> Events { get; }
}

/// <summary>
/// Represents a typed stream of events that can be read.
/// </summary>
/// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
public interface IReadOnlyStream<out T> : IReadOnlyStream
    where T : IState
{
    /// <summary>
    /// The state rebuilt from the stream events.
    /// </summary>
    T State { get; }
}

