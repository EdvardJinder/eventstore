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
}
