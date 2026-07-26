using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Configures the stable identity of an entity-outbox subscription.
/// </summary>
public sealed class OutboxSubscriptionRegistrationOptions
{
    private readonly HashSet<string> _logicalEventTypes = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _eventTypes = [];
    private readonly HashSet<Guid> _tenantIds = [];
    private readonly HashSet<string> _sourceEntityTypes = new(StringComparer.Ordinal);
    private readonly HashSet<EntityChangeKind> _changeKinds = [];

    /// <summary>
    /// Gets or sets the stable name used by checkpoints, locks, status APIs, logs, and metrics.
    /// When omitted, the subscription's assembly-qualified CLR type name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the behavior for events that cannot be materialized.</summary>
    public UnknownEventPolicy UnknownEventPolicy { get; set; } = UnknownEventPolicy.Fail;

    internal Func<UnknownOutboxEventContext, CancellationToken, ValueTask>? UnknownEventHandler { get; private set; }

    /// <summary>Includes events with the specified logical event type.</summary>
    /// <param name="logicalEventType">The non-empty logical event type.</param>
    public void IncludeLogicalEventType(string logicalEventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalEventType);
        _logicalEventTypes.Add(logicalEventType);
    }

    /// <summary>Includes events whose materialized payload has the specified CLR type.</summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    public void IncludeEventType<TEvent>()
        where TEvent : class =>
        _eventTypes.Add(typeof(TEvent));

    /// <summary>Includes events for the specified tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public void IncludeTenant(Guid tenantId) => _tenantIds.Add(tenantId);

    /// <summary>Includes events captured from the specified entity CLR type.</summary>
    /// <typeparam name="TEntity">The source entity type.</typeparam>
    public void IncludeSourceEntity<TEntity>()
        where TEntity : class =>
        _sourceEntityTypes.Add(typeof(TEntity).AssemblyQualifiedName!);

    /// <summary>Includes events captured from the specified persisted entity type name.</summary>
    /// <param name="sourceEntityType">The assembly-qualified source entity type name.</param>
    public void IncludeSourceEntityType(string sourceEntityType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityType);
        _sourceEntityTypes.Add(sourceEntityType);
    }

    /// <summary>Includes events produced by the specified entity change kind.</summary>
    /// <param name="changeKind">The entity change kind.</param>
    public void IncludeChangeKind(EntityChangeKind changeKind) => _changeKinds.Add(changeKind);

    /// <summary>
    /// Configures a custom handler for events that cannot be materialized.
    /// The checkpoint advances after the handler completes successfully.
    /// </summary>
    /// <param name="handler">The custom unknown-event handler.</param>
    public void HandleUnknown(
        Func<UnknownOutboxEventContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        UnknownEventHandler = handler;
        UnknownEventPolicy = UnknownEventPolicy.Custom;
    }

    internal void IncludeEventType(Type eventType) => _eventTypes.Add(eventType);

    internal bool MatchesPersisted(DbOutboxMessage message) =>
        (_logicalEventTypes.Count == 0 || _logicalEventTypes.Contains(message.TypeName)) &&
        (_tenantIds.Count == 0 || _tenantIds.Contains(message.TenantId)) &&
        (_sourceEntityTypes.Count == 0 || _sourceEntityTypes.Contains(message.SourceEntityType)) &&
        (_changeKinds.Count == 0 || _changeKinds.Contains(message.ChangeKind));

    internal bool MatchesMaterialized(Type eventType) =>
        _eventTypes.Count == 0 || _eventTypes.Contains(eventType);
}

internal sealed record OutboxSubscriptionRegistration(
    string Name,
    Type SubscriptionType,
    OutboxSubscriptionRegistrationOptions Options,
    Func<IServiceProvider, IOutboxSubscription> Resolve);

internal sealed class TypedOutboxSubscriptionAdapter<TSubscription, TEvent>(
    TSubscription subscription) : IOutboxSubscription
    where TSubscription : class, IOutboxSubscription<TEvent>
    where TEvent : class
{
    public Task Handle(IOutboxEvent @event, CancellationToken ct) =>
        subscription.Handle((IOutboxEvent<TEvent>)@event, ct);
}
