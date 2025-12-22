namespace EventStoreCore.Abstractions;

public interface IState
{
    public void Apply(IEvent @event);
}
