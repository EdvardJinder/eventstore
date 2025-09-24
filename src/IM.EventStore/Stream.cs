using Microsoft.EntityFrameworkCore;

namespace IM.EventStore;


internal class Stream(DbStream dbStream, DbContext db) : IStream
{
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
