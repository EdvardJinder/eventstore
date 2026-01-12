using EventStoreCore.Abstractions;

namespace EventStoreCore.Persistence.EntityFrameworkCore;

public sealed class ProjectionOptions : IProjectionOptions
{
    private readonly HashSet<Type> _handledEventTypes = new();
    private bool HandlesAllEvents = true;
    private int _version = 1;

    /// <summary>
    /// Gets the configured version for this projection.
    /// </summary>
    internal int ProjectionVersion => _version;

    /// <summary>
    /// Sets the version of the projection. When the version changes,
    /// the projection daemon can automatically trigger a rebuild.
    /// </summary>
    /// <param name="version">The version number. Increment to trigger rebuild.</param>
    public void Version(int version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        _version = version;
    }

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
