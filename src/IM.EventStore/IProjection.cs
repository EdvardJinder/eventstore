using Microsoft.EntityFrameworkCore;

namespace IM.EventStore;


public interface IProjection<TSnapshot> 
    where TSnapshot : class, new()
{
    /// <summary>
    /// Asynchronously applies the specified event to the given snapshot
    /// </summary>
    /// <remarks>This method does not return a value; it performs the evolution and persistence of the
    /// snapshot as a side effect. The operation is performed asynchronously and may be cancelled via the provided
    /// cancellation token. Do not call SaveChanges on the db context.</remarks>
    /// <param name="snapshot">The snapshot instance to be updated with the event. Cannot be null.</param>
    /// <param name="event">The event to apply to the snapshot. Cannot be null.</param>
    /// <param name="db">The database context used to persist the evolved snapshot. Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous evolve operation.</returns>
    static abstract Task Evolve(TSnapshot snapshot, IEvent @event, DbContext db, CancellationToken ct);
}

