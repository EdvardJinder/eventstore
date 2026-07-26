using System.Text.Json;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EventStoreCore;

internal sealed class EntityOutboxBuilder<TDbContext> : IEntityOutboxBuilder
    where TDbContext : DbContext
{
    private readonly Dictionary<Type, IEntityOutboxRegistration> _registrations = [];
    private readonly List<EventTypeRegistration> _eventTypes = [];

    public JsonSerializerOptions SerializerOptions { get; } = new();

    public IEntityOutboxEntityBuilder<TEntity> For<TEntity>()
        where TEntity : class
    {
        if (_registrations.TryGetValue(typeof(TEntity), out var existing))
        {
            return (EntityOutboxEntityBuilder<TEntity>)existing;
        }

        var registration = new EntityOutboxEntityBuilder<TEntity>();
        _registrations.Add(typeof(TEntity), registration);
        return registration;
    }

    public IEntityOutboxBuilder AddEvent<TEvent>(string eventTypeName)
        where TEvent : class
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        _eventTypes.Add(new EventTypeRegistration(typeof(TEvent), eventTypeName.Trim()));
        return this;
    }

    internal IReadOnlyList<EventTypeRegistration> EventTypes => _eventTypes;

    internal EntityOutboxRegistry<TDbContext> Build()
    {
        return new EntityOutboxRegistry<TDbContext>(_registrations, SerializerOptions);
    }
}

internal interface IEntityOutboxRegistration
{
    Type EntityType { get; }

    IReadOnlyList<object> CreateEvents(EntityEntry entry, EntityChangeKind changeKind);

    Guid GetTenantId(object entity);
}

internal sealed class EntityOutboxEntityBuilder<TEntity> :
    IEntityOutboxEntityBuilder<TEntity>,
    IEntityOutboxChangeBuilder<TEntity>,
    IEntityOutboxRegistration
    where TEntity : class
{
    private readonly Dictionary<EntityChangeKind, List<Func<EntityChange<TEntity>, IEnumerable<object?>>>> _factories = [];
    private Func<TEntity, Guid> _tenantSelector = _ => Guid.Empty;

    public Type EntityType => typeof(TEntity);

    public IEntityOutboxEntityBuilder<TEntity> On(Action<IEntityOutboxChangeBuilder<TEntity>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this);
        return this;
    }

    public IEntityOutboxEntityBuilder<TEntity> TenantId(Func<TEntity, Guid> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _tenantSelector = selector;
        return this;
    }

    public IEntityOutboxChangeBuilder<TEntity> Added(Func<EntityChange<TEntity>, object?> factory)
        => AddSingle(EntityChangeKind.Added, factory);

    public IEntityOutboxChangeBuilder<TEntity> AddedMany(Func<EntityChange<TEntity>, IEnumerable<object?>> factory)
        => AddMany(EntityChangeKind.Added, factory);

    public IEntityOutboxChangeBuilder<TEntity> Modified(Func<EntityChange<TEntity>, object?> factory)
        => AddSingle(EntityChangeKind.Modified, factory);

    public IEntityOutboxChangeBuilder<TEntity> ModifiedMany(Func<EntityChange<TEntity>, IEnumerable<object?>> factory)
        => AddMany(EntityChangeKind.Modified, factory);

    public IEntityOutboxChangeBuilder<TEntity> Deleted(Func<EntityChange<TEntity>, object?> factory)
        => AddSingle(EntityChangeKind.Deleted, factory);

    public IEntityOutboxChangeBuilder<TEntity> DeletedMany(Func<EntityChange<TEntity>, IEnumerable<object?>> factory)
        => AddMany(EntityChangeKind.Deleted, factory);

    public IReadOnlyList<object> CreateEvents(EntityEntry entry, EntityChangeKind changeKind)
    {
        if (!_factories.TryGetValue(changeKind, out var factories))
        {
            return [];
        }

        var typedEntry = entry.Context.Entry((TEntity)entry.Entity);
        var change = new EntityChange<TEntity>(typedEntry);
        return factories
            .SelectMany(factory => factory(change))
            .Where(@event => @event is not null)
            .Cast<object>()
            .ToArray();
    }

    public Guid GetTenantId(object entity) => _tenantSelector((TEntity)entity);

    private IEntityOutboxChangeBuilder<TEntity> AddSingle(
        EntityChangeKind changeKind,
        Func<EntityChange<TEntity>, object?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddMany(changeKind, change =>
        {
            var @event = factory(change);
            return @event is null ? [] : [@event];
        });
    }

    private IEntityOutboxChangeBuilder<TEntity> AddMany(
        EntityChangeKind changeKind,
        Func<EntityChange<TEntity>, IEnumerable<object?>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (!_factories.TryGetValue(changeKind, out var factories))
        {
            factories = [];
            _factories.Add(changeKind, factories);
        }

        factories.Add(change => factory(change)
            ?? throw new InvalidOperationException("An entity outbox event factory returned a null collection."));
        return this;
    }
}

internal sealed class EntityOutboxRegistry<TDbContext>(
    IReadOnlyDictionary<Type, IEntityOutboxRegistration> registrations,
    JsonSerializerOptions serializerOptions)
    where TDbContext : DbContext
{
    internal IReadOnlyDictionary<Type, IEntityOutboxRegistration> Registrations { get; } = registrations;

    internal JsonSerializerOptions SerializerOptions { get; } = serializerOptions;
}
