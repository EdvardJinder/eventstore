using System.Runtime.CompilerServices;
using System.Text.Json;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EventStoreCore;

internal sealed class EntityOutboxCapture<TDbContext>(
    EntityOutboxRegistry<TDbContext> registry,
    EventTypeRegistry eventTypes,
    TimeProvider timeProvider)
    where TDbContext : DbContext
{
    private readonly ConditionalWeakTable<DbContext, CaptureState> _states = new();

    internal void Capture(DbContext dbContext)
    {
        while (CaptureNext(dbContext))
        {
        }
    }

    internal bool CaptureNext(DbContext dbContext)
    {
        dbContext.ChangeTracker.DetectChanges();

        var state = _states.GetValue(dbContext, _ => new CaptureState());
        var entry = dbContext.ChangeTracker.Entries()
            .FirstOrDefault(entry =>
                (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                registry.Registrations.ContainsKey(entry.Metadata.ClrType) &&
                !state.Entities.Contains(entry.Entity));
        if (entry is null)
        {
            return false;
        }

        var registration = registry.Registrations[entry.Metadata.ClrType];
        var changeKind = ToChangeKind(entry.State);
        var events = registration.CreateEvents(entry, changeKind);

        foreach (var @event in events)
        {
            var eventType = @event.GetType();
            if (eventType.IsValueType)
            {
                throw new InvalidOperationException(
                    $"Entity outbox event type '{eventType}' must be a reference type.");
            }

            var message = new DbOutboxMessage
            {
                EventId = Guid.NewGuid(),
                TenantId = registration.GetTenantId(entry.Entity),
                Type = eventType.AssemblyQualifiedName
                    ?? throw new InvalidOperationException($"Event type '{eventType}' has no assembly-qualified name."),
                TypeName = eventTypes.ResolveName(eventType),
                Data = JsonSerializer.Serialize(@event, eventType, registry.SerializerOptions),
                Timestamp = timeProvider.GetUtcNow(),
                SourceEntityType = registration.EntityType.AssemblyQualifiedName
                    ?? registration.EntityType.FullName
                    ?? registration.EntityType.Name,
                SourceEntityKey = SerializeKey(entry, registry.SerializerOptions),
                ChangeKind = changeKind
            };

            dbContext.Set<DbOutboxMessage>().Add(message);
            state.Events.Add(new CapturedOutboxEvent(message, eventType, @event));
        }

        state.Entities.Add(entry.Entity);
        return true;
    }

    internal IReadOnlyList<CapturedOutboxEvent> GetEvents(DbContext dbContext) =>
        _states.TryGetValue(dbContext, out var state) ? state.Events : [];

    internal void Clear(DbContext dbContext) => _states.Remove(dbContext);

    private static string SerializeKey(EntityEntry entry, JsonSerializerOptions serializerOptions)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{entry.Metadata.ClrType.FullName}' must have a primary key to emit outbox events.");

        var values = primaryKey.Properties.ToDictionary(
            property => property.Name,
            property => entry.Property(property.Name).CurrentValue);

        return JsonSerializer.Serialize(values, serializerOptions);
    }

    private static EntityChangeKind ToChangeKind(EntityState state) => state switch
    {
        EntityState.Added => EntityChangeKind.Added,
        EntityState.Modified => EntityChangeKind.Modified,
        EntityState.Deleted => EntityChangeKind.Deleted,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private sealed class CaptureState
    {
        internal HashSet<object> Entities { get; } = new(ReferenceEqualityComparer.Instance);

        internal List<CapturedOutboxEvent> Events { get; } = [];
    }
}

internal sealed record CapturedOutboxEvent(DbOutboxMessage Message, Type EventType, object Data);
