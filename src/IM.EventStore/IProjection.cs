namespace IM.EventStore;


public interface IProjection<TSnapshot> 
    where TSnapshot : class, new()
{
    Task Evolve(TSnapshot snapshot, IEvent @event, CancellationToken ct);
}

