using Azure.Messaging;
using Azure.Messaging.EventGrid;
using EventStoreCore.CloudEvents;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.EventGrid;

/// <summary>
/// Publishes events to Azure Event Grid.
/// </summary>
/// <param name="serviceProvider">Service provider used to resolve the EventGrid publisher client.</param>
public class EventGridSubscription(IServiceProvider serviceProvider) : ICloudEventSubscription
{
    private readonly IServiceProvider sp = serviceProvider;

    /// <summary>
    /// Sends a CloudEvent to Event Grid.
    /// </summary>
    /// <param name="event">The CloudEvent to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task Handle(CloudEvent @event, CancellationToken ct)
    {
        var publisher = sp.GetRequiredService<EventGridPublisherClient>();
        return publisher.SendEventAsync(@event, ct);
    }
}


