namespace EventStoreCore;

internal sealed class EventTypeRegistry
{
    private readonly Dictionary<Type, string> _nameByType = new();
    private readonly Dictionary<string, Type> _typeByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nameByAqn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameByFullName = new(StringComparer.Ordinal);

    internal EventTypeRegistry(IEnumerable<EventTypeRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            Register(registration.EventType, registration.EventTypeName);
        }
    }

    internal bool TryGetType(string typeName, out Type eventType)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            eventType = null!;
            return false;
        }

        typeName = typeName.Trim();

        return _typeByName.TryGetValue(typeName, out eventType!);
    }

    internal bool TryGetName(Type eventType, out string typeName)
    {
        if (eventType is null)
        {
            typeName = string.Empty;
            return false;
        }

        return _nameByType.TryGetValue(eventType, out typeName!);
    }

    internal bool TryGetName(string? assemblyQualifiedName, out string typeName)
    {
        typeName = string.Empty;

        if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
        {
            return false;
        }

        assemblyQualifiedName = assemblyQualifiedName.Trim();

        if (_nameByAqn.TryGetValue(assemblyQualifiedName, out typeName))
        {
            return true;
        }

        var fullName = EventTypeNameHelper.GetFullNameFromAqn(assemblyQualifiedName);
        if (!string.IsNullOrWhiteSpace(fullName) && _nameByFullName.TryGetValue(fullName, out typeName))
        {
            return true;
        }

        return false;
    }

    internal string ResolveName(Type eventType)
    {
        if (TryGetName(eventType, out var name))
        {
            return name;
        }

        return EventTypeNameHelper.ToSnakeCase(eventType);
    }

    private void Register(Type eventType, string eventTypeName)
    {
        if (eventType is null)
        {
            throw new ArgumentNullException(nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        eventTypeName = eventTypeName.Trim();

        if (_nameByType.TryGetValue(eventType, out var existingName))
        {
            if (!string.Equals(existingName, eventTypeName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Event type '{eventType.FullName ?? eventType.Name}' is already registered with name '{existingName}'.");
            }

            return;
        }

        if (_typeByName.TryGetValue(eventTypeName, out var existingType) && existingType != eventType)
        {
            throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is already registered for '{existingType.FullName ?? existingType.Name}'.");
        }

        _nameByType[eventType] = eventTypeName;
        _typeByName[eventTypeName] = eventType;

        if (!string.IsNullOrWhiteSpace(eventType.AssemblyQualifiedName))
        {
            _nameByAqn[eventType.AssemblyQualifiedName!] = eventTypeName;
        }

        if (!string.IsNullOrWhiteSpace(eventType.FullName))
        {
            _nameByFullName[eventType.FullName!] = eventTypeName;
        }
    }
}

internal sealed record EventTypeRegistration(Type EventType, string EventTypeName);
