namespace EventStoreCore;

/// <summary>
/// Controls how projections are executed.
/// </summary>
public enum ProjectionMode
{
    /// <summary>
    /// Executes projections inline during SaveChanges.
    /// </summary>
    Inline,

    /// <summary>
    /// Executes projections asynchronously via the projection daemon.
    /// </summary>
    Eventual
}

