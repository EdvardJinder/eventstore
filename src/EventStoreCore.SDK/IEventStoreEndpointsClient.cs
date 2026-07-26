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
    /// Gets the tenant-scoped status of all registered projections.
    /// </summary>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of projection statuses for the tenant.</returns>
    [Get("/projections")]
    Task<IReadOnlyList<ProjectionStatusDto>> GetAllProjectionsForTenantAsync([Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a specific projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. A missing projection is represented by a 404 status.</returns>
    [Get("/projections/{name}")]
    Task<ApiResponse<ProjectionStatusDto>> GetProjectionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant-scoped status of a specific projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. A missing projection is represented by a 404 status.</returns>
    [Get("/projections/{name}")]
    Task<ApiResponse<ProjectionStatusDto>> GetProjectionForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

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
    /// Pauses tenant-scoped processing of the specified projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/pause")]
    Task PauseProjectionForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resumes processing of a paused projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/resume")]
    Task ResumeAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Resumes tenant-scoped processing of a paused projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/resume")]
    Task ResumeProjectionForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed event for a faulted projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. Missing failed-event details are represented by a 404 status.</returns>
    [Get("/projections/{name}/failed-event")]
    Task<ApiResponse<FailedEventDto>> GetFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed event for a tenant-scoped faulted projection.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. Missing failed-event details are represented by a 404 status.</returns>
    [Get("/projections/{name}/failed-event")]
    Task<ApiResponse<FailedEventDto>> GetFailedEventForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed event.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/retry")]
    Task RetryFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed tenant-scoped event.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/retry")]
    Task RetryFailedEventForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed event and resumes processing.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/skip")]
    Task SkipFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed tenant-scoped event and resumes processing.
    /// </summary>
    /// <param name="name">The projection name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/projections/{name}/skip")]
    Task SkipFailedEventForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of all registered subscriptions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of subscription statuses.</returns>
    [Get("/subscriptions")]
    Task<IReadOnlyList<SubscriptionStatusDto>> GetAllSubscriptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant-scoped status of all registered subscriptions.
    /// </summary>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of subscription statuses for the tenant.</returns>
    [Get("/subscriptions")]
    Task<IReadOnlyList<SubscriptionStatusDto>> GetAllSubscriptionsForTenantAsync([Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a specific subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. A missing subscription is represented by a 404 status.</returns>
    [Get("/subscriptions/{name}")]
    Task<ApiResponse<SubscriptionStatusDto>> GetSubscriptionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant-scoped status of a specific subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. A missing subscription is represented by a 404 status.</returns>
    [Get("/subscriptions/{name}")]
    Task<ApiResponse<SubscriptionStatusDto>> GetSubscriptionForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Pauses processing of the specified subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/pause")]
    Task PauseSubscriptionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Pauses tenant-scoped processing of the specified subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/pause")]
    Task PauseSubscriptionForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resumes processing of a paused subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/resume")]
    Task ResumeSubscriptionAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Resumes tenant-scoped processing of a paused subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/resume")]
    Task ResumeSubscriptionForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed event for a faulted or dead-lettered subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. Missing failed-event details are represented by a 404 status.</returns>
    [Get("/subscriptions/{name}/failed-event")]
    Task<ApiResponse<SubscriptionFailedEventDto>> GetSubscriptionFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets details about the failed event for a tenant-scoped faulted or dead-lettered subscription.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response. Missing failed-event details are represented by a 404 status.</returns>
    [Get("/subscriptions/{name}/failed-event")]
    Task<ApiResponse<SubscriptionFailedEventDto>> GetSubscriptionFailedEventForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed subscription event.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/retry")]
    Task RetrySubscriptionFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Retries processing the failed tenant-scoped subscription event.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/retry")]
    Task RetrySubscriptionFailedEventForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed subscription event and resumes processing.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/skip")]
    Task SkipSubscriptionFailedEventAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Skips the failed tenant-scoped subscription event and resumes processing.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/skip")]
    Task SkipSubscriptionFailedEventForTenantAsync(string name, [Query] Guid tenantId, CancellationToken ct = default);

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

    /// <summary>
    /// Replays a tenant-scoped subscription from a specific sequence or timestamp.
    /// </summary>
    /// <param name="name">The subscription name.</param>
    /// <param name="tenantId">The tenant identifier for the checkpoint scope.</param>
    /// <param name="startSequence">The starting sequence (inclusive) for replay.</param>
    /// <param name="fromTimestamp">Replay events starting at or after this timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    [Post("/subscriptions/{name}/replay")]
    Task ReplaySubscriptionForTenantAsync(
        string name,
        [Query] Guid tenantId,
        [Query] long? startSequence = null,
        [Query] DateTimeOffset? fromTimestamp = null,
        CancellationToken ct = default);
}


