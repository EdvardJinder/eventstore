namespace IM.EventStore;

public interface IStream : IReadOnlyStream
{
    public void Append(params IEnumerable<object> events);
}

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

public interface IStream<T> : IReadOnlyStream<T>
    where T : IState
{
    public void Append(params IEnumerable<object> events);
}

public interface IState
{
    public void Apply(IEvent @event);
}