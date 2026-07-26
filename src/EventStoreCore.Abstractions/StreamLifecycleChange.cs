namespace EventStoreCore.Abstractions;

/// <summary>
/// Supplies the audit metadata required for a stream lifecycle transition.
/// </summary>
public sealed class StreamLifecycleChange
{
    /// <summary>
    /// Gets or sets the human or service identity that authorized the transition.
    /// </summary>
    public required string Actor { get; set; }

    /// <summary>
    /// Gets or sets the reason for the transition.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Gets or sets an optional application correlation identifier.
    /// </summary>
    public string? CorrelationId { get; set; }
}
