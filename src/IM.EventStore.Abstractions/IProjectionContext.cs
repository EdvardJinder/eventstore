namespace IM.EventStore.Abstractions;

/// <summary>
/// Provides provider-specific context to projection execution.
/// </summary>
public interface IProjectionContext
{
    /// <summary>
    /// Service provider available for resolving dependencies during projection execution.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Provider-specific state, such as an EF DbContext instance.
    /// </summary>
    object? ProviderState { get; }
}
