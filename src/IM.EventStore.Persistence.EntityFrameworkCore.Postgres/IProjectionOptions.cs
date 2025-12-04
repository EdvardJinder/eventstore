using IM.EventStore.Abstractions;

namespace IM.EventStore.Persistence.EntityFrameworkCore.Postgres;

public interface IProjectionOptions
{
    void Handles<T>() where T : class;
    void HandlesAll();
    void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = default) where TEvent : class;
}
