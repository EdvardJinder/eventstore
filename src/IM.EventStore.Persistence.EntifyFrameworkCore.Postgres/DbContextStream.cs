using IM.EventStore.Abstractions;
using IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;

internal class DbContextStream(DbStream dbStream, DbContext db) : IStream
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

            dbStream.Events.Add(dbEvent);
        }

        dbStream.UpdatedTimestamp = DateTimeOffset.UtcNow;
    }
}

internal class DbContextStream<T>(DbStream dbStream, DbContext db) : DbContextStream(dbStream, db), IStream<T> where T : IState, new()
{
    public T State
    {
        get
        {
            var state = new T();
            foreach (var @event in Events)
            {
                state.Apply(@event);
            }
            return state;
        }
    }
}
