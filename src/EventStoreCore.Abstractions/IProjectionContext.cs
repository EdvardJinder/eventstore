namespace EventStoreCore.Abstractions;

/// <summary>
/// Provides provider-specific context to projection execution.
/// </summary>
public interface IProjectionContext
{
    /// <summary>
    /// Service provider available for resolving dependencies during projection execution.
    /// Prefer local persistence dependencies over remote side-effecting services.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Provider-specific state, such as an EF DbContext instance.
    /// When projections run inline, this state participates in the same persistence scope as the event append.
    /// </summary>
    object? ProviderState { get; }
}
