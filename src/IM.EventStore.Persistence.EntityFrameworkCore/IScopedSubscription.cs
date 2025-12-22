using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IM.EventStore.Persistence.EntityFrameworkCore;

/// <summary>
/// A subscription that prefers to run with an existing DbContext/transaction scope.
/// </summary>
public interface IScopedSubscription : ISubscription
{
    Task HandleAsync(DbContext dbContext, IEvent @event, CancellationToken ct);
}
