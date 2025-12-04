using IM.EventStore.Abstractions;

namespace IM.EventStore.Persistence.EntifyFrameworkCore.Postgres;

internal sealed class ProjectionOptions : IProjectionOptions
{
    private readonly HashSet<Type> _handledEventTypes = new();
    private bool HandlesAllEvents = true;
    public void Handles<T>() where T : class
    {
        HandlesAllEvents = false;
        _handledEventTypes.Add(typeof(T));
    }
    public void HandlesAll()
    {
        HandlesAllEvents = true;
    }

    public bool IsHandeled(Type eventType)
    {
        return HandlesAllEvents || _handledEventTypes.Contains(eventType);
    }

    private Dictionary<Type, Func<IEvent<object>, object>> _keySelectors = new();
    private void KeySelector<TEvent>(Func<IEvent<TEvent>, object> keySelector) where TEvent : class
    {
        _keySelectors[typeof(TEvent)] = e => keySelector((IEvent<TEvent>)e);
    }

    internal Func<IEvent<object>, object> GetKeySelector(Type eventType)
    {
        if (_keySelectors.TryGetValue(eventType, out var selector))
        {
            return e => selector(e);
        }
        return e => e.StreamId;
    }
    public void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = null) where TEvent : class
    {
        Handles<TEvent>();
        if (keySelector is not null)
        {
            KeySelector(keySelector);
        }
    }
}