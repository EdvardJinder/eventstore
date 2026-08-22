using System.Runtime.CompilerServices;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventStoreCore;

internal sealed class InlineEventHandlerInterceptor<TDbContext>(
    IServiceProvider serviceProvider,
    InlineEventHandlerConfiguration<TDbContext> configuration,
    EntityOutboxCapture<TDbContext>? outboxCapture,
    EventTypeRegistry? eventTypes,
    IEventStoreSerializer? serializer) : SaveChangesInterceptor
    where TDbContext : DbContext
{
    private readonly ConditionalWeakTable<DbContext, DispatchState> _states = new();
    private readonly IEventStoreSerializer _serializer =
        serializer ?? new SystemTextJsonEventStoreSerializer();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        DispatchAsync(eventData.Context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await DispatchAsync(eventData.Context, cancellationToken);
        return result;
    }

    private async Task DispatchAsync(DbContext? dbContext, CancellationToken ct)
    {
        if (dbContext is null)
        {
            return;
        }

        var state = _states.GetValue(dbContext, _ => new DispatchState());
        if (state.IsDispatching)
        {
            throw new InvalidOperationException(
                "Inline event handlers cannot call SaveChanges. Mutate tracked state and let the outer save commit it.");
        }

        var scopedContext = serviceProvider.GetService(typeof(TDbContext));
        if (!ReferenceEquals(scopedContext, dbContext))
        {
            throw new InvalidOperationException(
                $"Inline event handlers for '{typeof(TDbContext).FullName}' must be resolved from the same DI scope as the committing DbContext.");
        }

        dbContext.ChangeTracker.DetectChanges();
        var initialStreamEventIds = dbContext.ChangeTracker.Entries<DbEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.EventId)
            .ToHashSet();
        var handled = new HashSet<InlineEventKey>();
        var dispatchedCount = 0;

        state.IsDispatching = true;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                EnsureNoStreamAppends(dbContext, initialStreamEventIds);

                var wave = GetWave(dbContext, handled);
                if (wave.Count == 0 && outboxCapture?.CaptureNext(dbContext) == true)
                {
                    wave = GetWave(dbContext, handled);
                }

                if (wave.Count == 0)
                {
                    break;
                }

                foreach (var sourceEvent in wave)
                {
                    handled.Add(sourceEvent.Key);
                    var registrations = configuration.Registrations
                        .Where(registration =>
                            registration.EventType == sourceEvent.Envelope.EventType &&
                            (registration.Sources & sourceEvent.Source) != 0)
                        .ToArray();
                    if (registrations.Length == 0)
                    {
                        continue;
                    }

                    dispatchedCount++;
                    if (dispatchedCount > configuration.MaxDispatchCount)
                    {
                        throw new InvalidOperationException(
                            $"Inline event dispatch exceeded the configured limit of {configuration.MaxDispatchCount} source envelopes.");
                    }

                    foreach (var registration in registrations)
                    {
                        await registration.Handle(serviceProvider, sourceEvent.Envelope, ct);
                        EnsureNoStreamAppends(dbContext, initialStreamEventIds);
                    }
                }
            }
        }
        finally
        {
            state.IsDispatching = false;
        }
    }

    private IReadOnlyList<SourceEvent> GetWave(
        DbContext dbContext,
        HashSet<InlineEventKey> handled)
    {
        var streamEvents = dbContext.ChangeTracker.Entries<DbEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .OrderBy(@event => @event.StreamType, StringComparer.Ordinal)
            .ThenBy(@event => @event.StreamId)
            .ThenBy(@event => @event.Version)
            .Select(@event => new SourceEvent(
                new InlineEventKey(InlineEventSource.Stream, @event.EventId),
                InlineEventSource.Stream,
                @event.ToEvent(eventTypes, _serializer)))
            .Where(@event => !handled.Contains(@event.Key));

        var outboxEvents = (outboxCapture?.GetEvents(dbContext) ?? [])
            .Select(captured => new SourceEvent(
                new InlineEventKey(InlineEventSource.EntityOutbox, captured.Message.EventId),
                InlineEventSource.EntityOutbox,
                new OutboxEvent(captured.Message, captured.EventType, captured.Data)))
            .Where(@event => !handled.Contains(@event.Key));

        return streamEvents.Concat(outboxEvents).ToArray();
    }

    private static void EnsureNoStreamAppends(
        DbContext dbContext,
        HashSet<Guid> initialStreamEventIds)
    {
        var appended = dbContext.ChangeTracker.Entries<DbEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.EventId)
            .FirstOrDefault(eventId => !initialStreamEventIds.Contains(eventId));
        if (appended != Guid.Empty)
        {
            throw new InvalidOperationException(
                "Inline event handlers cannot append stream events. Use a command boundary or durable subscription for cross-stream reactions.");
        }
    }

    private sealed class DispatchState
    {
        internal bool IsDispatching { get; set; }
    }

    private readonly record struct InlineEventKey(InlineEventSource Source, Guid Id);

    private sealed record SourceEvent(
        InlineEventKey Key,
        InlineEventSource Source,
        IEventEnvelope Envelope);
}
