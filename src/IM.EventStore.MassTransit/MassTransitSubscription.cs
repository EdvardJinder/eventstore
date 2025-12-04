using IM.EventStore.Abstractions;
using MassTransit;
using MassTransit.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IM.EventStore.MassTransit;

internal class MassTransitSubscription : ISubscription
{
    internal class EventLogScopeState
    {
        public Guid StreamId { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventId { get; set; }

    }

    private readonly IServiceProvider sp;

    public MassTransitSubscription(IServiceProvider sp)
    {
        this.sp = sp;
    }

    public async Task Handle(IEvent @event, CancellationToken ct)
    {
        var scope = sp.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MassTransitSubscription>>();

        using var _ = logger.BeginScope(new EventLogScopeState
        {
            StreamId = @event.StreamId,
            EventId = @event.Id,
            TenantId = @event.TenantId
        });

        var bus = scope.ServiceProvider.GetRequiredService<IBus>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<EventTransformerOptions>>();
        var handlers = options.Value.Handlers;

        if(!handlers.TryGetValue(@event.EventType, out var handler))
        {
            logger.LogDebug("No handler for {EventType}", @event.EventType);
            return;
        }

        var outType = handler.Out;
        var transform = handler.Transform;

        var eventData = transform(@event);

        if(eventData is null)
        {
            logger.LogError("Transform returned null for {EventType}", @event.EventType);
            return;
        }

        logger.LogDebug("Publishing transformed event for {EventType}", @event.EventType);
        await bus.Publish(eventData, outType, ct);
        logger.LogDebug("Published transformed event successfully");

    }
}
