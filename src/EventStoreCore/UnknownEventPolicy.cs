namespace EventStoreCore;

/// <summary>
/// Defines how a subscription handles an event whose persisted CLR type cannot be resolved or materialized.
/// </summary>
public enum UnknownEventPolicy
{
    /// <summary>Records the failure and applies the subscription retry policy.</summary>
    Fail = 0,

    /// <summary>Skips the event and advances the subscription checkpoint.</summary>
    Skip = 1,

    /// <summary>Immediately moves the subscription checkpoint to the dead-lettered state.</summary>
    Quarantine = 2,

    /// <summary>Invokes the configured custom handler and advances the checkpoint when it succeeds.</summary>
    Custom = 3
}
