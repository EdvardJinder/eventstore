using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore;

internal class Projection<TSnapshot, TProjection, TDbContext> : ISubscription
    where TSnapshot : class, new()
    where TProjection : IProjection<TSnapshot>
    where TDbContext : DbContext
{
    private readonly IDbContextFactory<TDbContext> _dbContextFactory;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public Projection(IDbContextFactory<TDbContext> dbContextFactory, IServiceScopeFactory serviceScopeFactory)
    {
        _dbContextFactory = dbContextFactory;
        _serviceScopeFactory = serviceScopeFactory;
    }
    public async Task OnEventAsync(IEvent @event, CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var projection = scope.ServiceProvider.GetRequiredService<TProjection>();
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbSet = dbContext.Set<TSnapshot>();
        var snapshot = await dbSet.FindAsync(@event.StreamId);
        if (snapshot is null)
        {
            snapshot = new TSnapshot();
            dbSet.Add(snapshot);
        }
        await projection.EvolveAsync(snapshot, @event, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
