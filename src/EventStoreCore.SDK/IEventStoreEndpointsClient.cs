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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of projection statuses.</returns>
    [Get("/projections")]
    Task<IReadOnlyList<ProjectionStatusDto>> GetAllProjectionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a specific projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The projection status, or null when not found.</returns>
    [Get("/projections/{name}")]
    Task<ProjectionStatusDto?> GetProjectionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Triggers a rebuild of the specified projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/rebuild")]
    Task RebuildAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Pauses processing of the specified projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/pause")]
    Task PauseAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Resumes processing of a paused projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/resume")]
    Task ResumeAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed event for a faulted projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The failed event details, or null when not found.</returns>
    [Get("/projections/{name}/failed-event")]
    Task<FailedEventDto?> GetFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed event.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/retry")]
    Task RetryFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed event and resumes processing.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/skip")]
    Task SkipFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of all registered subscriptions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of subscription statuses.</returns>
    [Get("/subscriptions")]
    Task<IReadOnlyList<SubscriptionStatusDto>> GetAllSubscriptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a specific subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subscription status, or null when not found.</returns>
    [Get("/subscriptions/{name}")]
    Task<SubscriptionStatusDto?> GetSubscriptionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Replays a subscription from a specific sequence or timestamp.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="startSequence">The starting sequence (inclusive) for replay.</param>
    /// <param name="fromTimestamp">Replay events starting at or after this timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/replay")]
    Task ReplaySubscriptionAsync(
        string name,
        [Query] long? startSequence = null,
        [Query] DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default);
}


