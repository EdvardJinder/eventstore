using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;


/// <summary>
/// Executes a projection in the subscription daemon scope (eventual consistency).
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
/// <typeparam name="TProjection">The projection implementation.</typeparam>
/// <typeparam name="TSnapshot">The snapshot type.</typeparam>
internal sealed class EventualProjectionSubscription<TDbContext, TProjection, TSnapshot> : IScopedSubscription
    where TDbContext : DbContext
    where TProjection : IProjection<TSnapshot>, new()
    where TSnapshot : class, new()
{
    private readonly ProjectionOptions _options;

    /// <summary>
    /// Creates a subscription that executes projections via the daemon pipeline.
    /// </summary>
    /// <param name="options">Projection options.</param>
    public EventualProjectionSubscription(ProjectionOptions options)
    {
        _options = options;
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
    /// <param name="services">The application service provider for the active daemon scope.</param>
    /// <param name="event">The event to process.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task HandleAsync(
        DbContext dbContext,
        IServiceProvider services,
        IEvent @event,
        CancellationToken ct)
    {
        if (!_options.IsHandled(@event.EventType))
        {
            return;
        }

        var keySelector = _options.GetKeySelector(@event.EventType);
        var key = keySelector((IEvent<object>)@event);

        var snapshot = await dbContext
            .Set<TSnapshot>()
            .FindAsync([key], ct);

        var projectionContext = new ProjectionContext(dbContext, services);

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

