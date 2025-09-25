using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

internal class ProjectionWrapper<TProjection, TSnapshot, TDbContext> : ISubscription
    where TProjection : IProjection<TSnapshot>
    where TSnapshot : class, new()
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public ProjectionWrapper(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public async Task HandleBatchAsync(IEvent[] events, CancellationToken ct)
    { 
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var projection = scope.ServiceProvider.GetRequiredService<TProjection>();

        foreach(var stream in events.GroupBy(e => e.StreamId))
        {
            var snaphost = await dbContext
                .Set<TSnapshot>()
                .FindAsync([stream.Key], ct);

            if (snaphost is null)
            {
                snaphost = new TSnapshot();
                foreach (var @event in stream.OrderBy(e => e.Version))
                {
                    await projection.Evolve(snaphost, @event, ct);
                }
                dbContext.Add(snaphost);
            }
            else
            {
                foreach (var @event in stream.OrderBy(e => e.Version))
                {
                    await projection.Evolve(snaphost, @event, ct);
                }
                dbContext.Update(snaphost);
            }
            await dbContext.SaveChangesAsync(ct);
        }
    }

   
}

