namespace EventStoreCore.Abstractions;

/// <summary>
/// Configures a bounded page or asynchronous stream read.
/// </summary>
public sealed class StreamReadOptions
{
    /// <summary>
    /// The inclusive starting stream version. Defaults to version 1 for forward reads and
    /// the captured current stream version for backward reads.
    /// </summary>
    public long? FromVersion { get; set; }

    /// <summary>
    /// The inclusive opposite boundary. Defaults to the captured current stream version for
    /// forward reads and version 1 for backward reads.
    /// </summary>
    public long? ToVersion { get; set; }

    /// <summary>
    /// The maximum number of events returned by one page. Defaults to 100.
    /// </summary>
    public int MaxCount { get; set; } = 100;

    /// <summary>
    /// The ordering direction. Defaults to <see cref="StreamReadDirection.Forward"/>.
    /// </summary>
    public StreamReadDirection Direction { get; set; } = StreamReadDirection.Forward;
}
