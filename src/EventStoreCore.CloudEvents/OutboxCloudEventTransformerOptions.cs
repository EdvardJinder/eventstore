using Azure.Messaging;
using EventStoreCore.Abstractions;

namespace EventStoreCore.CloudEvents;

/// <summary>
/// Configures mappings from entity-outbox events to CloudEvents.
/// </summary>
public sealed class OutboxCloudEventTransformerOptions
{
    internal Dictionary<Type, Func<IOutboxEvent, CloudEvent>> Mappings { get; } = [];

    internal HashSet<Type> PreservedCloudEventIds { get; } = [];

    /// <summary>
    /// Registers a custom CloudEvent mapping for the given outbox event type.
    /// </summary>
    /// <typeparam name="TEvent">The outbox event payload type.</typeparam>
    /// <param name="transform">Transformation function.</param>
    /// <param name="preserveCloudEventId">
    /// Whether to preserve the ID returned by <paramref name="transform"/>.
    /// The default replaces it with the stable outbox event ID.
    /// </param>
    public void MapEvent<TEvent>(
        Func<IOutboxEvent<TEvent>, CloudEvent> transform,
        bool preserveCloudEventId = false)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(transform);

        Mappings[typeof(TEvent)] = @event => transform((IOutboxEvent<TEvent>)@event);
        if (preserveCloudEventId)
        {
            PreservedCloudEventIds.Add(typeof(TEvent));
        }
        else
        {
            PreservedCloudEventIds.Remove(typeof(TEvent));
        }
    }

    /// <summary>
    /// Registers a CloudEvent mapping using the provided metadata and subject selector.
    /// </summary>
    /// <typeparam name="TEvent">The outbox event payload type.</typeparam>
    /// <param name="type">The CloudEvent type.</param>
    /// <param name="source">The CloudEvent source.</param>
    /// <param name="subject">Function used to create the CloudEvent subject.</param>
    public void MapEvent<TEvent>(
        string type,
        string source,
        Func<IOutboxEvent<TEvent>, string> subject)
        where TEvent : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(subject);

        MapEvent<TEvent>(@event => new CloudEvent(
            source,
            type,
            @event.Data,
            @event.EventType)
        {
            Time = @event.Timestamp,
            Subject = subject(@event)
        });
    }
}
