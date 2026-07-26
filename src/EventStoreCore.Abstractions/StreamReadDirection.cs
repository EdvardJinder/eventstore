namespace EventStoreCore.Abstractions;

/// <summary>
/// Specifies event ordering for a bounded stream read.
/// </summary>
public enum StreamReadDirection
{
    /// <summary>
    /// Read from lower stream versions to higher stream versions.
    /// </summary>
    Forward = 0,

    /// <summary>
    /// Read from higher stream versions to lower stream versions.
    /// </summary>
    Backward = 1
}
