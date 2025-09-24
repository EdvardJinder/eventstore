namespace IM.EventStore;

public interface IInlineProjection<TSnapshot>
    where TSnapshot : class, new()
{
    void Evolve(TSnapshot snapshot, IEvent @event);
}
