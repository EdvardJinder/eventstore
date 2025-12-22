using Azure.Messaging;
using EventStoreCore.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace EventStoreCore.CloudEvents;

internal sealed class CloudEventTransformer(IOptions<CloudEventTransformerOptions> options)
{
    public bool TryTransform(IEvent @event, [NotNullWhen(true)] out CloudEvent? cloudEvent)
    {
        if (options is not null && options.Value._mappings.TryGetValue(@event.EventType, out var transform))
        {
            cloudEvent = transform(@event);
            return true;
        }

        cloudEvent = null;
        return false;
    }
}
