using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

/// <summary>
/// Configures domain events captured from EF entity changes.
/// </summary>
public interface IEntityOutboxBuilder
{
    /// <summary>
    /// Configures capture rules for an entity type.
    /// </summary>
    IEntityOutboxEntityBuilder<TEntity> For<TEntity>()
        where TEntity : class;

    /// <summary>
    /// Registers a stable logical name for an outbox event payload type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="eventTypeName">The stable logical event type name.</param>
    /// <returns>The builder for chaining.</returns>
    IEntityOutboxBuilder AddEvent<TEvent>(string eventTypeName)
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
    IEntityOutboxEntityBuilder<TEntity> On(Action<IEntityOutboxChangeBuilder<TEntity>> configure);

    /// <summary>
    /// Selects the tenant id stored with events from this entity.
    /// </summary>
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
    IEntityOutboxChangeBuilder<TEntity> Added(Func<EntityChange<TEntity>, object?> factory);

    /// <summary>
    /// Adds a zero-or-many event factory for added entities.
    /// </summary>
    IEntityOutboxChangeBuilder<TEntity> AddedMany(Func<EntityChange<TEntity>, IEnumerable<object?>> factory);

    /// <summary>
    /// Adds a zero-or-one event factory for modified entities. Return <see langword="null" /> to suppress emission.
    /// </summary>
    IEntityOutboxChangeBuilder<TEntity> Modified(Func<EntityChange<TEntity>, object?> factory);

    /// <summary>
    /// Adds a zero-or-many event factory for modified entities.
    /// </summary>
    IEntityOutboxChangeBuilder<TEntity> ModifiedMany(Func<EntityChange<TEntity>, IEnumerable<object?>> factory);

    /// <summary>
    /// Adds a zero-or-one event factory for deleted entities. Return <see langword="null" /> to suppress emission.
    /// </summary>
    IEntityOutboxChangeBuilder<TEntity> Deleted(Func<EntityChange<TEntity>, object?> factory);

    /// <summary>
    /// Adds a zero-or-many event factory for deleted entities.
    /// </summary>
    IEntityOutboxChangeBuilder<TEntity> DeletedMany(Func<EntityChange<TEntity>, IEnumerable<object?>> factory);
}
