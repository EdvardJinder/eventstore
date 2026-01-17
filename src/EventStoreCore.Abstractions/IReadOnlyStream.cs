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
    /// The current stream version.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// The events in the stream, ordered by version.
    /// </summary>
    IReadOnlyList<IEvent> Events { get; }
}

/// <summary>
/// Represents a typed stream of events that can be read.
/// </summary>
/// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
public interface IReadOnlyStream<T>
    where T : IState
{
    /// <summary>
    /// The stream identifier.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The current stream version.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// The events in the stream, ordered by version.
    /// </summary>
    IReadOnlyList<IEvent> Events { get; }

    /// <summary>
    /// The state rebuilt from the stream events.
    /// </summary>
    T State { get; }
}

