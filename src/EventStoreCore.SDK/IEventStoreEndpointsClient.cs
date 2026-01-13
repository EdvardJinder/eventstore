using EventStoreCore.Abstractions;
using Refit;

namespace EventStoreCore.SDK;


/// <summary>
/// Refit interface for the EventStore API.
/// </summary>
public interface IEventStoreEndpointsClient
{
    /// <summary>
    /// Gets the status of all registered projections.
    /// </summary>
    [Get("/projections")]
    Task<IReadOnlyList<ProjectionStatusDto>> GetAllProjectionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a specific projection.
    /// </summary>
    [Get("/projections/{name}")]
    Task<ProjectionStatusDto?> GetProjectionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Triggers a rebuild of the specified projection.
    /// </summary>
    [Post("/projections/{name}/rebuild")]
    Task RebuildAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Pauses processing of the specified projection.
    /// </summary>
    [Post("/projections/{name}/pause")]
    Task PauseAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Resumes processing of a paused projection.
    /// </summary>
    [Post("/projections/{name}/resume")]
    Task ResumeAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed event for a faulted projection.
    /// </summary>
    [Get("/projections/{name}/failed-event")]
    Task<FailedEventDto?> GetFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed event.
    /// </summary>
    [Post("/projections/{name}/retry")]
    Task RetryFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed event and resumes processing.
    /// </summary>
    [Post("/projections/{name}/skip")]
    Task SkipFailedEventAsync(string name, CancellationToken ct = default);
}
