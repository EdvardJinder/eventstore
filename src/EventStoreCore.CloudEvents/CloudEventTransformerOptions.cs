using Azure.Messaging;
using EventStoreCore.Abstractions;

namespace EventStoreCore.CloudEvents;

/// <summary>
/// Configuration for mapping events to CloudEvents.
/// </summary>
public sealed class CloudEventTransformerOptions
{
    internal Dictionary<Type, Func<IEvent, CloudEvent>> _mappings = new();
    internal HashSet<Type> _preservedCloudEventIds = [];

    /// <summary>
    /// Registers a custom CloudEvent mapping for the given event type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="transform">Transformation function.</param>
    /// <param name="preserveCloudEventId">
    /// Whether to preserve the ID returned by <paramref name="transform"/>.
    /// The default replaces it with the stable EventStore event ID.
    /// </param>
    public void MapEvent<TEvent>(
        Func<IEvent<TEvent>, CloudEvent> transform,
        bool preserveCloudEventId = false)
        where TEvent : class
    {
        _mappings[typeof(TEvent)] = (ievent) => transform((IEvent<TEvent>)ievent);
        if (preserveCloudEventId)
        {
            _preservedCloudEventIds.Add(typeof(TEvent));
        }
        else
        {
            _preservedCloudEventIds.Remove(typeof(TEvent));
        }
    }

    /// <summary>
    /// Registers a CloudEvent mapping using the provided metadata and subject selector.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="type">The CloudEvent type.</param>
    /// <param name="source">The CloudEvent source.</param>
    /// <param name="subject">Function to generate the CloudEvent subject.</param>
    public void MapEvent<TEvent>(string type, string source, Func<IEvent<TEvent>, string> subject)
       where TEvent : class
    {
        _mappings[typeof(TEvent)] = (ievent) =>
        {
            return new CloudEvent(
                source: source,
                type: type,
                jsonSerializableData: ((IEvent<TEvent>)ievent).Data,
                dataSerializationType: ievent.EventType
                )
            {
                Time = ievent.Timestamp,
                Subject = subject((IEvent<TEvent>)ievent),
                ExtensionAttributes =
                {
               ["tenantid"] = ievent.TenantId.ToString()
                }
            };
        };
    }
}

