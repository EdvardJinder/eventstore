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

        var eventType = Type.GetType(dbEvent.Type);
        if (eventType is not null)
        {
            if (registry is not null && !string.IsNullOrWhiteSpace(dbEvent.TypeName))
            {
                if (registry.TryGetType(dbEvent.TypeName, out var mappedType) && mappedType != eventType)
                {
                    throw new EventMaterializationException(
                        $"Event type name '{dbEvent.TypeName}' is registered for '{mappedType.FullName ?? mappedType.Name}', " +
                        $"but event record contains '{eventType.FullName ?? eventType.Name}'.",
                        dbEvent);
                }
            }

            return eventType;
        }

        if (registry is not null && !string.IsNullOrWhiteSpace(dbEvent.TypeName))
        {
            if (registry.TryGetType(dbEvent.TypeName, out var mappedType))
            {
                return mappedType;
            }
        }

        throw new EventMaterializationException(
            $"Could not resolve event type for TypeName '{dbEvent.TypeName}' and Type '{dbEvent.Type}'.",
            dbEvent);
    }
}
