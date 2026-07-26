namespace EventStoreCore;

/// <summary>
/// Configures aggregate snapshot behavior for a state type.
/// </summary>
public sealed class SnapshotOptions
{
    /// <summary>
    /// The stream version interval at which snapshots are written.
    /// </summary>
    public int Interval { get; set; } = 100;

    /// <summary>
    /// The schema version written with new snapshots. Increment this when the state or serializer shape
    /// is no longer compatible with stored snapshots.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// The behavior used when a stored snapshot has a different schema version.
    /// </summary>
    public SnapshotSchemaMismatchBehavior OnSchemaMismatch { get; set; } =
        SnapshotSchemaMismatchBehavior.Rebuild;
}
