namespace EventStoreCore;

internal static class EventTypeResolver
{
    internal static Type ResolveEventType(DbEvent dbEvent, EventTypeRegistry? registry)
    {
        if (registry is not null && !string.IsNullOrWhiteSpace(dbEvent.TypeName))
        {
            if (registry.TryGetType(dbEvent.TypeName, out var mappedType))
            {
                return mappedType;
            }
        }

        if (!string.IsNullOrWhiteSpace(dbEvent.Type))
        {
            var eventType = Type.GetType(dbEvent.Type);
            if (eventType is not null)
            {
                return eventType;
            }
        }

        throw new EventMaterializationException(
            $"Could not resolve event type for TypeName '{dbEvent.TypeName}' and Type '{dbEvent.Type}'.",
            dbEvent);
    }
}
