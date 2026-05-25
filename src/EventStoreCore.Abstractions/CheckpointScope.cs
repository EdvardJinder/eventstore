namespace EventStoreCore.Abstractions;

/// <summary>
/// Defines how daemon checkpoint rows are shared across tenants.
/// </summary>
public enum CheckpointScope
{
    /// <summary>
    /// A single checkpoint row is shared across all tenants.
    /// </summary>
    Global = 0,

    /// <summary>
    /// Each tenant has an independent checkpoint row.
    /// </summary>
    Tenant = 1
}
