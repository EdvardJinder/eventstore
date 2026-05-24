namespace EventStoreCore;

/// <summary>
/// Controls how projections are executed.
/// </summary>
public enum ProjectionMode
{
    /// <summary>
    /// Executes projections inline during SaveChanges using the caller's DbContext and transaction.
    /// Use this mode for deterministic, idempotent, local projection work that must commit with the append.
    /// </summary>
    Inline,

    /// <summary>
    /// Executes projections asynchronously via the projection daemon.
    /// This mode is eventually consistent and follows at-least-once delivery semantics.
    /// </summary>
    Eventual
}

