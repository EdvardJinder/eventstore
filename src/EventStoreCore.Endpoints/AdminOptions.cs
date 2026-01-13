using Microsoft.AspNetCore.Http;

namespace EventStoreCore.Admin;

/// <summary>
/// Configuration options for the EventStore admin API endpoints.
/// </summary>
public sealed class AdminOptions
{
    /// <summary>
    /// The route prefix for admin endpoints.
    /// Default is "/api/eventstore/admin".
    /// </summary>
    public string RoutePrefix { get; set; } = "/api/eventstore/admin";

    /// <summary>
    /// Optional authorization callback. Return true to allow access, false to deny.
    /// If not set, endpoints are accessible to anyone.
    /// </summary>
    public Func<HttpContext, Task<bool>>? AuthorizeAsync { get; set; }

    /// <summary>
    /// Optional authorization policy name to apply to all admin endpoints.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }
}
