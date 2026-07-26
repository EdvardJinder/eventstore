namespace EventStoreCore;

/// <summary>
/// Configures a subscription's stable identity, filters, and unknown-event behavior.
/// Filtered events advance the checkpoint without invoking the handler.
/// </summary>
public sealed class SubscriptionRegistrationOptions
{
    private readonly HashSet<string> _logicalEventTypes = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _eventTypes = [];
    private readonly HashSet<string> _streamTypes = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _streamIds = [];
    private readonly HashSet<Guid> _tenantIds = [];

    /// <summary>
    /// Gets or sets the stable identity used by checkpoints, locks, status APIs, and logs.
    /// When omitted, the subscription's assembly-qualified CLR type name is used for compatibility.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the behavior for unresolvable or unmaterializable events.</summary>
    public UnknownEventPolicy UnknownEventPolicy { get; set; } = UnknownEventPolicy.Fail;

    internal Func<UnknownEventContext, CancellationToken, ValueTask>? UnknownEventHandler { get; private set; }

    /// <summary>
    /// Includes events with the specified logical event type.
    /// Multiple values in one category are combined with OR; categories are combined with AND.
    /// </summary>
    /// <param name="logicalEventType">The non-empty logical event type name.</param>
    public void IncludeLogicalEventType(string logicalEventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalEventType);
        _logicalEventTypes.Add(logicalEventType);
    }

    /// <summary>Includes events whose materialized payload has the specified CLR type.</summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    public void IncludeEventType<TEvent>() where TEvent : class => _eventTypes.Add(typeof(TEvent));

    /// <summary>Includes events from the specified logical stream type.</summary>
    /// <param name="streamType">The stream type, including an empty string for the default stream type.</param>
    public void IncludeStreamType(string streamType)
    {
        ArgumentNullException.ThrowIfNull(streamType);
        _streamTypes.Add(streamType);
    }

    /// <summary>Includes events from the specified stream identifier.</summary>
    /// <param name="streamId">The stream identifier.</param>
    public void IncludeStream(Guid streamId) => _streamIds.Add(streamId);

    /// <summary>Includes events for the specified tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public void IncludeTenant(Guid tenantId) => _tenantIds.Add(tenantId);

    /// <summary>
    /// Configures a custom handler for events that cannot be materialized.
    /// The checkpoint advances after the handler completes successfully.
    /// </summary>
    /// <param name="handler">The custom handler.</param>
    public void HandleUnknown(Func<UnknownEventContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        UnknownEventHandler = handler;
        UnknownEventPolicy = UnknownEventPolicy.Custom;
    }

    internal void IncludeEventType(Type eventType) => _eventTypes.Add(eventType);

    internal bool MatchesPersisted(DbEvent @event) =>
        (_logicalEventTypes.Count == 0 || _logicalEventTypes.Contains(@event.TypeName)) &&
        (_streamTypes.Count == 0 || _streamTypes.Contains(@event.StreamType)) &&
        (_streamIds.Count == 0 || _streamIds.Contains(@event.StreamId)) &&
        (_tenantIds.Count == 0 || _tenantIds.Contains(@event.TenantId));

    internal bool MatchesMaterialized(Type eventType) =>
        _eventTypes.Count == 0 || _eventTypes.Contains(eventType);
}
