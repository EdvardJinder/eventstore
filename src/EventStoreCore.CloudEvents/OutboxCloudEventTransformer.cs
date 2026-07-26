using System.Diagnostics.CodeAnalysis;
using Azure.Messaging;
using EventStoreCore.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStoreCore.CloudEvents;

internal sealed class OutboxCloudEventTransformer(
    IOptions<OutboxCloudEventTransformerOptions> options)
{
    internal bool TryTransform(
        IOutboxEvent @event,
        [NotNullWhen(true)] out CloudEvent? cloudEvent)
    {
        if (!options.Value.Mappings.TryGetValue(@event.EventType, out var transform))
        {
            cloudEvent = null;
            return false;
        }

        cloudEvent = transform(@event)
            ?? throw new InvalidOperationException(
                $"CloudEvent transform returned null for outbox event type '{@event.EventType}'.");

        if (!options.Value.PreservedCloudEventIds.Contains(@event.EventType))
        {
            cloudEvent.Id = @event.Id.ToString("D");
        }

        cloudEvent.ExtensionAttributes.TryAdd("tenantid", @event.TenantId.ToString("D"));
        cloudEvent.ExtensionAttributes.TryAdd("outboxsequence", @event.Sequence.ToString());
        cloudEvent.ExtensionAttributes.TryAdd("sourceentitytype", @event.SourceEntityType);
        cloudEvent.ExtensionAttributes.TryAdd("sourceentitykey", @event.SourceEntityKey);
        cloudEvent.ExtensionAttributes.TryAdd("entitychangekind", @event.ChangeKind.ToString());
        return true;
    }
}
