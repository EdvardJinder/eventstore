namespace IM.EventStore.Abstractions;

public interface IStream : IReadOnlyStream
{
    public void Append(params IEnumerable<object> events);
}

public interface IStream<T> : IReadOnlyStream<T>
    where T : IState
{
    public void Append(params IEnumerable<object> events);
}
