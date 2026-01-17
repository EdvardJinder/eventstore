using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;


/// <summary>
/// Executes a projection in the subscription daemon scope (eventual consistency).
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
/// <typeparam name="TProjection">The projection implementation.</typeparam>
/// <typeparam name="TSnapshot">The snapshot type.</typeparam>
public sealed class EventualProjectionSubscription<TDbContext, TProjection, TSnapshot> : IScopedSubscription
    where TDbContext : DbContext
    where TProjection : IProjection<TSnapshot>, new()
    where TSnapshot : class, new()
{
    private readonly ProjectionOptions _options;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates a subscription that executes projections via the daemon pipeline.
    /// </summary>
    /// <param name="options">Projection options.</param>
    /// <param name="services">Service provider for resolving dependencies.</param>
    public EventualProjectionSubscription(ProjectionOptions options, IServiceProvider services)
    {
        _options = options;
        _services = services;
    }

    /// <summary>
    /// This subscription must run with a DbContext scope; use <see cref="HandleAsync" /> instead.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="NotSupportedException">Thrown because daemon execution requires a scoped DbContext.</exception>
    public Task Handle(IEvent @event, CancellationToken ct)
    {
        // This subscription expects to run with a DbContext via IScopedSubscription.HandleAsync
        throw new NotSupportedException("Use the subscription daemon path to execute this projection.");
    }

    /// <summary>
    /// Handles an event using the projection and DbContext scope.
    /// </summary>
    /// <param name="dbContext">The DbContext scope used for persistence.</param>
    /// <param name="event">The event to process.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task HandleAsync(DbContext dbContext, IEvent @event, CancellationToken ct)
    {
        if (!_options.IsHandeled(@event.EventType))
        {
            return;
        }

        var keySelector = _options.GetKeySelector(@event.EventType);
        var key = keySelector((IEvent<object>)@event);

        var snapshot = await dbContext
            .Set<TSnapshot>()
            .FindAsync([key], ct);

        var projectionContext = new ProjectionContext(dbContext, _services);

        if (snapshot is null)
        {
            snapshot = new TSnapshot();
            await TProjection.Evolve(snapshot, @event, projectionContext, ct);
            dbContext.Add(snapshot);
        }
        else
        {
            await TProjection.Evolve(snapshot, @event, projectionContext, ct);
        }
    }
}

