namespace EventStoreCore;

internal static class EventTypeResolver
{
    internal static Type ResolveEventType(DbEvent dbEvent, EventTypeRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(dbEvent);

        if (string.IsNullOrWhiteSpace(dbEvent.Type))
        {
            throw new EventMaterializationException("Event type is required.", dbEvent);
        }

        if (registry is not null
            && !string.IsNullOrWhiteSpace(dbEvent.TypeName)
            && registry.TryResolveMaterializedEventType(dbEvent.TypeName, out var registeredType))
        {
            return registeredType;
        }

        Type? eventType;
        try
        {
            eventType = Type.GetType(dbEvent.Type);
        }
        catch (Exception ex)
        {
            throw new EventMaterializationException(
                $"Could not resolve event type from malformed Type string '{dbEvent.Type}'.",
                dbEvent,
                ex);
        }

        if (eventType is not null)
            return eventType;

        if (registry is not null
            && !string.IsNullOrWhiteSpace(dbEvent.TypeName)
            && registry.TryGetType(dbEvent.TypeName, out var registryType))
        {
            return registryType;
        }

        throw new EventMaterializationException(
            $"Could not resolve event type for TypeName '{dbEvent.TypeName}' and Type '{dbEvent.Type}'.",
            dbEvent);
    }
}
