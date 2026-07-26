using System.Runtime.CompilerServices;
using System.Text.Json;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventStoreCore;

internal sealed class EntityOutboxInterceptor<TDbContext>(
    EntityOutboxRegistry<TDbContext> registry,
    EventTypeRegistry eventTypes,
    TimeProvider timeProvider) : SaveChangesInterceptor
    where TDbContext : DbContext
{
    private static readonly ConditionalWeakTable<DbContext, HashSet<object>> CapturedEntities = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Clear(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Clear(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        dbContext.ChangeTracker.DetectChanges();

        var alreadyCaptured = CapturedEntities.GetValue(
            dbContext,
            _ => new HashSet<object>(ReferenceEqualityComparer.Instance));
        var entries = dbContext.ChangeTracker.Entries()
            .Where(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                registry.Registrations.ContainsKey(entry.Metadata.ClrType) &&
                !alreadyCaptured.Contains(entry.Entity))
            .ToArray();

        foreach (var entry in entries)
        {
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

                dbContext.Set<DbOutboxMessage>().Add(new DbOutboxMessage
                {
                    EventId = Guid.NewGuid(),
                    TenantId = registration.GetTenantId(entry.Entity),
                    Type = eventType.AssemblyQualifiedName
                        ?? throw new InvalidOperationException($"Event type '{eventType}' has no assembly-qualified name."),
                    TypeName = eventTypes.ResolveName(eventType),
                    Data = JsonSerializer.Serialize(@event, eventType, registry.SerializerOptions),
                    Timestamp = timeProvider.GetUtcNow(),
                    EntityType = registration.EntityType.AssemblyQualifiedName
                        ?? registration.EntityType.FullName
                        ?? registration.EntityType.Name,
                    EntityKey = SerializeKey(entry, registry.SerializerOptions),
                    ChangeKind = changeKind
                });
            }

            alreadyCaptured.Add(entry.Entity);
        }
    }

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

    private static void Clear(DbContext? dbContext)
    {
        if (dbContext is not null)
        {
            CapturedEntities.Remove(dbContext);
        }
    }
}
