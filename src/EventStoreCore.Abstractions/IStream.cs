namespace EventStoreCore.Abstractions;

/// <summary>
/// Represents a stream that can append new events.
/// </summary>
public interface IStream : IReadOnlyStream
{
    /// <summary>
    /// Appends one or more events to the stream.
    /// </summary>
    /// <param name="events">Events to append.</param>
    void Append(params IEnumerable<object> events);
}

/// <summary>
/// Represents a typed stream that can append new events.
/// </summary>
/// <typeparam name="T">The state type reconstructed from the stream.</typeparam>
public interface IStream<T> : IReadOnlyStream<T>
    where T : IState
{
    /// <summary>
    /// Appends one or more events to the stream.
    /// </summary>
    /// <param name="events">Events to append.</param>
    void Append(params IEnumerable<object> events);
}

