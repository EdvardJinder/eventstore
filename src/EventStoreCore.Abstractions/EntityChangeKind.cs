namespace EventStoreCore.Abstractions;

/// <summary>
/// Identifies the EF entity state transition that produced an outbox event.
/// </summary>
public enum EntityChangeKind
{
    /// <summary>
    /// The entity was added.
    /// </summary>
    Added = 0,

    /// <summary>
    /// The entity was modified.
    /// </summary>
    Modified = 1,

    /// <summary>
    /// The entity was deleted.
    /// </summary>
    Deleted = 2
}
