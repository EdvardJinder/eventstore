using EventStoreCore.Abstractions;

namespace EventStoreCore;


internal sealed class ProjectionOptions : IProjectionOptions
{
    private readonly HashSet<Type> _handledEventTypes = new();
    private readonly HashSet<Type> _ignoredEventTypes = new();
    private readonly HashSet<string> _logicalEventTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _streamTypes = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _streamIds = [];
    private readonly HashSet<Guid> _tenantIds = [];
    private bool HandlesAllEvents = true;
    private bool _ignoreUnknown;

    internal string? LogicalName { get; private set; }

    /// <inheritdoc />
    public void Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LogicalName = name;
    }

    /// <inheritdoc />
    public void IncludeLogicalEventType(string logicalEventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalEventType);
        _logicalEventTypes.Add(logicalEventType);
    }

    /// <inheritdoc />
    public void IncludeStreamType(string streamType)
    {
        ArgumentNullException.ThrowIfNull(streamType);
        _streamTypes.Add(streamType);
    }

    /// <inheritdoc />
    public void IncludeStream(Guid streamId) => _streamIds.Add(streamId);

    /// <inheritdoc />
    public void IncludeTenant(Guid tenantId) => _tenantIds.Add(tenantId);

    /// <summary>
    /// Registers a handled event type.
    /// </summary>
    /// <typeparam name="T">The event payload type.</typeparam>
    public void Handles<T>() where T : class
    {
        HandlesAllEvents = false;
        _handledEventTypes.Add(typeof(T));
    }

    /// <summary>
    /// Marks the projection as handling all event types.
    /// </summary>
    public void HandlesAll()
    {
        HandlesAllEvents = true;
    }

    /// <summary>
    /// Excludes a specific event type from processing.
    /// </summary>
    /// <typeparam name="T">The event payload type to ignore.</typeparam>
    public void Ignores<T>() where T : class
    {
        _ignoredEventTypes.Add(typeof(T));
    }

    /// <summary>
    /// Instructs the projection to skip events whose CLR type cannot be resolved
    /// instead of throwing an exception.
    /// </summary>
    public void IgnoreUnknown()
    {
        _ignoreUnknown = true;
    }

    /// <summary>
    /// Gets whether unresolvable event types should be silently skipped.
    /// True when <see cref="IgnoreUnknown"/> was called or the projection uses explicit <see cref="Handles{T}()" /> registration.
    /// </summary>
    internal bool ShouldIgnoreUnknown => _ignoreUnknown || !HandlesAllEvents;

    internal bool IsHandled(Type eventType)
    {
        if (_ignoredEventTypes.Contains(eventType))
        {
            return false;
        }

        return HandlesAllEvents || _handledEventTypes.Contains(eventType);
    }

    internal bool MatchesPersisted(DbEvent @event) =>
        (_logicalEventTypes.Count == 0 || _logicalEventTypes.Contains(@event.TypeName)) &&
        (_streamTypes.Count == 0 || _streamTypes.Contains(@event.StreamType)) &&
        (_streamIds.Count == 0 || _streamIds.Contains(@event.StreamId)) &&
        (_tenantIds.Count == 0 || _tenantIds.Contains(@event.TenantId));

    internal bool Matches(IEvent @event) =>
        (_logicalEventTypes.Count == 0 || _logicalEventTypes.Contains(@event.TypeName)) &&
        (_streamTypes.Count == 0 || _streamTypes.Contains(@event.StreamType)) &&
        (_streamIds.Count == 0 || _streamIds.Contains(@event.StreamId)) &&
        (_tenantIds.Count == 0 || _tenantIds.Contains(@event.TenantId)) &&
        IsHandled(@event.EventType);

    internal IQueryable<DbEvent> ApplyPersistedFilters(IQueryable<DbEvent> query)
    {
        if (_logicalEventTypes.Count > 0)
        {
            var logicalEventTypes = _logicalEventTypes.ToArray();
            query = query.Where(@event => logicalEventTypes.Contains(@event.TypeName));
        }

        if (_streamTypes.Count > 0)
        {
            var streamTypes = _streamTypes.ToArray();
            query = query.Where(@event => streamTypes.Contains(@event.StreamType));
        }

        if (_streamIds.Count > 0)
        {
            var streamIds = _streamIds.ToArray();
            query = query.Where(@event => streamIds.Contains(@event.StreamId));
        }

        if (_tenantIds.Count > 0)
        {
            var tenantIds = _tenantIds.ToArray();
            query = query.Where(@event => tenantIds.Contains(@event.TenantId));
        }

        return query;
    }

    private readonly Dictionary<Type, Func<IEvent<object>, object>> _keySelectors = new();
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

    /// <summary>
    /// Registers a handled event type with an optional snapshot key selector.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="keySelector">Selects a snapshot key for the event.</param>
    public void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = null) where TEvent : class
    {
        Handles<TEvent>();
        if (keySelector is not null)
        {
            KeySelector(keySelector);
        }
    }
}
