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