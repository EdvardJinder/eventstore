using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Persistence.EntityFrameworkCore;

/// <summary>
/// A subscription that prefers to run with an existing DbContext/transaction scope.
/// </summary>
public interface IScopedSubscription : ISubscription
{
    Task HandleAsync(DbContext dbContext, IEvent @event, CancellationToken ct);
}
