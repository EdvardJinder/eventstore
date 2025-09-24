namespace IM.EventStore;

public interface IProjection<TSnapshot>
    where TSnapshot : class, new()
{
    Task EvolveAsync(TSnapshot snapshot, IEvent @event, CancellationToken cancellationToken);
}
