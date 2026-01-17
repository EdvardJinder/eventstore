using Azure.Messaging;

namespace EventStoreCore.CloudEvents;

/// <summary>
/// Handles CloudEvents emitted by the event store.
/// </summary>
public interface ICloudEventSubscription 
{
     /// <summary>
     /// Processes a CloudEvent.
     /// </summary>
     /// <param name="event">The CloudEvent to handle.</param>
     /// <param name="ct">Cancellation token.</param>
     Task Handle(CloudEvent @event, CancellationToken ct);
}

