using EventStoreCore.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace EventStoreCore.Admin;

/// <summary>
/// Extension methods for mapping EventStore admin endpoints.
/// </summary>
public static class EventStoreAdminExtensions
{
    /// <summary>
    /// Maps the EventStore admin API endpoints.
    /// Use MapGroup on the IEndpointRouteBuilder to set a custom route prefix before calling this method.
    /// Call RequireAuthorization on the returned RouteGroupBuilder to add authorization.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The route group builder for further customization.</returns>
    public static RouteGroupBuilder MapEventStoreAdmin(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty);

        // GET /projections - List all projections
        group.MapGet("/projections", async ([FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            var statuses = await manager.GetAllStatusesAsync(ct);
            return Results.Ok(statuses);
        })
        .WithName("GetAllProjections")
        .WithDescription("Gets the status of all registered projections")
        .Produces<IReadOnlyList<ProjectionStatusDto>>();

        // GET /projections/{name} - Get specific projection
        group.MapGet("/projections/{name}", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            var status = await manager.GetStatusAsync(name, ct);
            return status != null ? Results.Ok(status) : Results.NotFound();
        })
        .WithName("GetProjection")
        .WithDescription("Gets the status of a specific projection")
        .Produces<ProjectionStatusDto>()
        .Produces(StatusCodes.Status404NotFound);

        // POST /projections/{name}/rebuild - Trigger rebuild
        group.MapPost("/projections/{name}/rebuild", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.RebuildAsync(name, ct);
                return Results.Accepted();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RebuildProjection")
        .WithDescription("Triggers a rebuild of the specified projection")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest);

        // POST /projections/{name}/pause - Pause projection
        group.MapPost("/projections/{name}/pause", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.PauseAsync(name, ct);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("PauseProjection")
        .WithDescription("Pauses processing of the specified projection")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // POST /projections/{name}/resume - Resume projection
        group.MapPost("/projections/{name}/resume", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.ResumeAsync(name, ct);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ResumeProjection")
        .WithDescription("Resumes processing of a paused projection")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // GET /projections/{name}/failed-event - Get failed event details
        group.MapGet("/projections/{name}/failed-event", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            var failedEvent = await manager.GetFailedEventAsync(name, ct);
            return failedEvent != null ? Results.Ok(failedEvent) : Results.NotFound();
        })
        .WithName("GetFailedEvent")
        .WithDescription("Gets details about the failed event for a faulted projection")
        .Produces<FailedEventDto>()
        .Produces(StatusCodes.Status404NotFound);

        // POST /projections/{name}/retry - Retry failed event
        group.MapPost("/projections/{name}/retry", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.RetryFailedEventAsync(name, ct);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RetryFailedEvent")
        .WithDescription("Retries processing the failed event")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // POST /projections/{name}/skip - Skip failed event
        group.MapPost("/projections/{name}/skip", async ([FromRoute] string name, [FromServices] IProjectionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.SkipFailedEventAsync(name, ct);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SkipFailedEvent")
        .WithDescription("Skips the failed event and resumes processing")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        return group;
    }
}
