using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IM.EventStore;


internal class Stream(DbStream dbStream, DbContext db) : IStream
{
    public Guid TenantId => dbStream.TenantId;
    public Guid Id => dbStream.Id;
    public long Version => dbStream.CurrentVersion;
    public IReadOnlyList<IEvent> Events => dbStream.Events
        .OrderBy(e => e.Version)
        .Select(e =>
        {
            var type = Type.GetType(e.Type!)!;
            var eventType = typeof(Event<>).MakeGenericType(type);
            return (IEvent)Activator.CreateInstance(eventType, e)!;
        })
        .ToList();
    public void Append(params IEnumerable<object> events)
    {
        foreach (var @event in events)
        {
            var dbEvent = new DbEvent
            {
                TenantId = dbStream.TenantId,
                StreamId = dbStream.Id,
                Version = ++dbStream.CurrentVersion,
                Type = @event.GetType().AssemblyQualifiedName!,
                Data = System.Text.Json.JsonSerializer.Serialize(@event),
                Timestamp = DateTimeOffset.UtcNow,
                EventId = Guid.NewGuid()
            };
            db.Set<DbEvent>().Add(dbEvent);
        }
        
        dbStream.UpdatedTimestamp = DateTimeOffset.UtcNow;
    }
}

internal class Stream<T>(DbStream dbStream, DbContext db) : Stream(dbStream, db), IStream<T> where T : IState, new()
{
    private T? _state;
    public T State
    {
        get
        {
            if (_state is null)
            {
                _state = new T();
                foreach (var @event in Events)
                {
                    _state.Apply(@event);
                }
            }
            return _state;
        }
    }
}