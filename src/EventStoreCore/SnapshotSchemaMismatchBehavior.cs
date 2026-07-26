namespace EventStoreCore;

/// <summary>
/// Determines how a typed read handles an incompatible persisted snapshot schema.
/// </summary>
public enum SnapshotSchemaMismatchBehavior
{
    /// <summary>
    /// Ignore the incompatible snapshot and rebuild state from the event history.
    /// </summary>
    Rebuild = 0,

    /// <summary>
    /// Throw instead of rebuilding, allowing an application-owned migration to run first.
    /// </summary>
    Throw = 1
}
