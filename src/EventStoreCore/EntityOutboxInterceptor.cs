using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventStoreCore;

internal sealed class EntityOutboxInterceptor<TDbContext>(
    EntityOutboxCapture<TDbContext> capture,
    bool deferCapture) : SaveChangesInterceptor
    where TDbContext : DbContext
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (!deferCapture && eventData.Context is DbContext dbContext)
        {
            capture.Capture(dbContext);
        }
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!deferCapture && eventData.Context is DbContext dbContext)
        {
            capture.Capture(dbContext);
        }
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is DbContext dbContext)
        {
            capture.Clear(dbContext);
        }
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is DbContext dbContext)
        {
            capture.Clear(dbContext);
        }
        return ValueTask.FromResult(result);
    }
}
