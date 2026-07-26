using EventStoreCore.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStoreCore.MassTransit;

internal sealed class MassTransitOutboxSubscription(IServiceProvider serviceProvider) : IOutboxSubscription
{
    internal sealed class EventLogScopeState
    {
        public Guid EventId { get; init; }

        public long Sequence { get; init; }

        public Guid TenantId { get; init; }

        public string SourceEntityType { get; init; } = string.Empty;
    }

    public async Task Handle(IOutboxEvent @event, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MassTransitOutboxSubscription>>();

        using var _ = logger.BeginScope(new EventLogScopeState
        {
            EventId = @event.Id,
            Sequence = @event.Sequence,
            TenantId = @event.TenantId,
            SourceEntityType = @event.SourceEntityType
        });

        var handlers = scope.ServiceProvider
            .GetRequiredService<IOptions<OutboxEventTransformerOptions>>()
            .Value
            .Handlers;

        if (!handlers.TryGetValue(@event.EventType, out var handlerList))
        {
            logger.LogDebug("No handler for outbox event type {EventType}", @event.EventType);
            return;
        }

        var bus = scope.ServiceProvider.GetRequiredService<IBus>();
        foreach (var handler in handlerList)
        {
            var message = handler.Transform(@event);
            if (message is null)
            {
                throw new InvalidOperationException(
                    $"Transform returned null for outbox event type '{@event.EventType}' (output type: '{handler.Out}').");
            }

            logger.LogDebug("Publishing transformed outbox event for {EventType}", @event.EventType);
            await bus.Publish(
                message,
                handler.Out,
                context =>
                {
                    context.MessageId = @event.Id;
                    context.Headers.Set("EventStore-TenantId", @event.TenantId);
                    context.Headers.Set("EventStore-OutboxSequence", @event.Sequence);
                    context.Headers.Set("EventStore-SourceEntityType", @event.SourceEntityType);
                    context.Headers.Set("EventStore-SourceEntityKey", @event.SourceEntityKey);
                    context.Headers.Set("EventStore-EntityChangeKind", @event.ChangeKind.ToString());
                },
                ct);
            logger.LogDebug("Published transformed outbox event successfully");
        }
    }
}
