namespace EventStoreCore.Abstractions;

/// <summary>
/// Identifies the governance state of an event stream.
/// </summary>
public enum StreamLifecycleState
{
    /// <summary>
    /// The stream can be read and appended to.
    /// </summary>
    Active,

    /// <summary>
    /// The stream remains readable but cannot be appended to until an administrator restores it.
    /// </summary>
    Archived,

    /// <summary>
    /// The stream is hidden from normal stream reads and cannot be appended to or restored.
    /// Its events remain physically retained for audit integrity and existing event-log consumers.
    /// </summary>
    Tombstoned
}
