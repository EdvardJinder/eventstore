namespace EJ.EventStore.Abstractions;


public interface IReadOnlyStream
{
    public Guid Id { get; }
    public long Version { get; }
    public IReadOnlyList<IEvent> Events { get; }
}
public interface IReadOnlyStream<T>
    where T : IState
{
    public Guid Id { get; }
    public long Version { get; }
    public IReadOnlyList<IEvent> Events { get; }
    public T State { get; }
}
