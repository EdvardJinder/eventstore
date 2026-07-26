using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

internal sealed class EntityOutboxReader<TDbContext>(
    TDbContext dbContext,
    EntityOutboxRegistry<TDbContext> registry,
    EventTypeRegistry eventTypes) : IOutboxReader
    where TDbContext : DbContext
{
    public async Task<IReadOnlyList<IOutboxEvent>> ReadAsync(
        long afterSequence,
        int maxCount = 100,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        if (afterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        }

        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        }

        var query = dbContext.Set<DbOutboxMessage>()
            .AsNoTracking()
            .Where(message => message.Sequence > afterSequence);

        if (tenantId.HasValue)
        {
            query = query.Where(message => message.TenantId == tenantId.Value);
        }

        var messages = await query
            .OrderBy(message => message.Sequence)
            .Take(maxCount)
            .ToListAsync(ct);

        return messages.Select(Materialize).ToArray();
    }

    public async Task<int> CleanupAsync(long throughSequence, CancellationToken ct = default)
    {
        if (throughSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(throughSequence));
        }

        var checkpoints = await dbContext.Set<DbOutboxSubscription>()
            .AsNoTracking()
            .Select(subscription => subscription.Sequence)
            .ToListAsync(ct);

        if (checkpoints.Count == 0)
        {
            return 0;
        }

        var safeSequence = Math.Min(throughSequence, checkpoints.Min());
        return await dbContext.Set<DbOutboxMessage>()
            .Where(message => message.Sequence <= safeSequence)
            .ExecuteDeleteAsync(ct);
    }

    internal IOutboxEvent Materialize(DbOutboxMessage message)
    {
        var (eventType, upcastData) = ResolveTypeAndUpcastData(message);
        object data = upcastData!;

        if (data is null)
        {
            try
            {
                data = JsonSerializer.Deserialize(message.Data, eventType, registry.SerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"Could not deserialize outbox event {message.EventId} as '{eventType}'.");
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Could not deserialize outbox event {message.EventId} as '{eventType}'.",
                    ex);
            }
        }

        var wrapperType = typeof(OutboxEvent<>).MakeGenericType(eventType);

        try
        {
            return (IOutboxEvent)(Activator.CreateInstance(
                wrapperType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [message, eventType, data],
                culture: CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"Could not create '{wrapperType}'."));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private (Type EventType, object? UpcastData) ResolveTypeAndUpcastData(DbOutboxMessage message)
    {
        var compatibilityEvent = new DbEvent
        {
            EventId = message.EventId,
            Sequence = message.Sequence,
            TenantId = message.TenantId,
            Timestamp = message.Timestamp,
            Type = message.Type,
            TypeName = message.TypeName,
            Data = message.Data
        };

        if (eventTypes.TryResolveMaterializedEvent(
            compatibilityEvent,
            registry.Serializer,
            out var registeredType,
            out var upcastData))
        {
            return (registeredType, upcastData);
        }

        var eventType = Type.GetType(message.Type, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Could not resolve CLR type '{message.Type}' for outbox event {message.EventId}.");
        return (eventType, null);
    }
}
