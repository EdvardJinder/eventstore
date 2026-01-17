using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;


/// <summary>
/// A subscription that prefers to run with an existing DbContext/transaction scope.
/// </summary>
public interface IScopedSubscription : ISubscription
{
    /// <summary>
    /// Processes an event using the provided DbContext scope.
    /// </summary>
    /// <param name="dbContext">The DbContext scope for persistence.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(DbContext dbContext, IEvent @event, CancellationToken ct);
}

