namespace IM.EventStore.Abstractions;

public interface IState
{
    public void Apply(IEvent @event);
}
