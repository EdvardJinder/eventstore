using System.Text.Json;

namespace EventStoreCore;

/// <summary>
/// Configures domain events captured from EF entity changes.
/// </summary>
public interface IEntityOutboxBuilder
{
    /// <summary>
    /// Configures capture rules for an entity type.
    /// </summary>
    /// <typeparam name="TEntity">The EF entity type.</typeparam>
    /// <returns>The entity builder for chaining.</returns>
    IEntityOutboxEntityBuilder<TEntity> For<TEntity>()
        where TEntity : class;

    /// <summary>
    /// Registers an outbox event payload type using its default snake_case logical name.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    IEntityOutboxBuilder AddEvent<TEvent>()
        where TEvent : class;

    /// <summary>
    /// Registers a stable logical name for an outbox event payload type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="eventTypeName">The stable logical event type name.</param>
    /// <returns>The builder for chaining.</returns>
    IEntityOutboxBuilder AddEvent<TEvent>(string eventTypeName)
        where TEvent : class;

    /// <summary>
    /// Registers a stable logical name and configures aliases or upcasters for an outbox event payload type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="eventTypeName">The stable logical event type name.</param>
    /// <param name="configure">Configures aliases and upcasters for the event type.</param>
    /// <returns>The builder for chaining.</returns>
    IEntityOutboxBuilder AddEvent<TEvent>(
        string eventTypeName,
        Action<IEventTypeBuilder<TEvent>>? configure)
        where TEvent : class;

    /// <summary>
    /// Gets the serializer options used for outbox payloads and entity keys.
    /// </summary>
    JsonSerializerOptions SerializerOptions { get; }
}

/// <summary>
/// Configures capture metadata and callbacks for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IEntityOutboxEntityBuilder<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Configures callbacks for entity state transitions.
    /// </summary>
    /// <param name="configure">Configures events emitted for added, modified, and deleted entities.</param>
    /// <returns>The entity builder for chaining.</returns>
    IEntityOutboxEntityBuilder<TEntity> On(Action<IEntityOutboxChangeBuilder<TEntity>> configure);

    /// <summary>
    /// Selects the tenant id stored with events from this entity.
    /// </summary>
    /// <param name="selector">Selects the tenant id from the tracked entity.</param>
    /// <returns>The entity builder for chaining.</returns>
    IEntityOutboxEntityBuilder<TEntity> TenantId(Func<TEntity, Guid> selector);
}

/// <summary>
/// Configures event factories for entity state transitions.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IEntityOutboxChangeBuilder<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Adds a zero-or-one event factory for added entities. Return <see langword="null" /> to suppress emission.
    /// </summary>
    /// <param name="factory">Creates the event for an added entity.</param>
    /// <returns>The change builder for chaining.</returns>
    IEntityOutboxChangeBuilder<TEntity> Added(Func<EntityChange<TEntity>, object?> factory);

    /// <summary>
    /// Adds a zero-or-many event factory for added entities. Return an empty collection to suppress emission.
    /// </summary>
    /// <param name="factory">Creates the events for an added entity.</param>
    /// <returns>The change builder for chaining.</returns>
    IEntityOutboxChangeBuilder<TEntity> Added(
        Func<EntityChange<TEntity>, IReadOnlyCollection<object>> factory);

    /// <summary>
    /// Adds a zero-or-one event factory for modified entities. Return <see langword="null" /> to suppress emission.
    /// </summary>
    /// <param name="factory">Creates the event for a modified entity.</param>
    /// <returns>The change builder for chaining.</returns>
    IEntityOutboxChangeBuilder<TEntity> Modified(Func<EntityChange<TEntity>, object?> factory);

    /// <summary>
    /// Adds a zero-or-many event factory for modified entities. Return an empty collection to suppress emission.
    /// </summary>
    /// <param name="factory">Creates the events for a modified entity.</param>
    /// <returns>The change builder for chaining.</returns>
    IEntityOutboxChangeBuilder<TEntity> Modified(
        Func<EntityChange<TEntity>, IReadOnlyCollection<object>> factory);

    /// <summary>
    /// Adds a zero-or-one event factory for deleted entities. Return <see langword="null" /> to suppress emission.
    /// </summary>
    /// <param name="factory">Creates the event for a deleted entity.</param>
    /// <returns>The change builder for chaining.</returns>
    IEntityOutboxChangeBuilder<TEntity> Deleted(Func<EntityChange<TEntity>, object?> factory);

    /// <summary>
    /// Adds a zero-or-many event factory for deleted entities. Return an empty collection to suppress emission.
    /// </summary>
    /// <param name="factory">Creates the events for a deleted entity.</param>
    /// <returns>The change builder for chaining.</returns>
    IEntityOutboxChangeBuilder<TEntity> Deleted(
        Func<EntityChange<TEntity>, IReadOnlyCollection<object>> factory);
}
