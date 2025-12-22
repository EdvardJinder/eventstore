using EJ.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EJ.EventStore.Persistence.EntityFrameworkCore;

/// <summary>
/// Executes a projection in the subscription daemon scope (eventual consistency).
/// </summary>
public sealed class EventualProjectionSubscription<TDbContext, TProjection, TSnapshot> : IScopedSubscription
    where TDbContext : DbContext
    where TProjection : IProjection<TSnapshot>, new()
    where TSnapshot : class, new()
{
    private readonly ProjectionOptions _options;
    private readonly IServiceProvider _services;

    public EventualProjectionSubscription(ProjectionOptions options, IServiceProvider services)
    {
        _options = options;
        _services = services;
    }

    public Task Handle(IEvent @event, CancellationToken ct)
    {
        // This subscription expects to run with a DbContext via IScopedSubscription.HandleAsync
        throw new NotSupportedException("Use the subscription daemon path to execute this projection.");
    }

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
