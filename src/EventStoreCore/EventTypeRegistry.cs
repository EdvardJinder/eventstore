namespace EventStoreCore;

internal sealed class EventTypeRegistry
{
    private readonly Dictionary<Type, string> _nameByType = new();
    private readonly Dictionary<string, Type> _typeByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _canonicalNameByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventUpcasterRegistration> _upcasterByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nameByAqn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameByFullName = new(StringComparer.Ordinal);

    internal EventTypeRegistry(IEnumerable<EventTypeRegistration> registrations)
        : this(registrations, [], [])
    {
    }

    internal EventTypeRegistry(
        IEnumerable<EventTypeRegistration> registrations,
        IEnumerable<EventTypeAliasRegistration> aliases,
        IEnumerable<EventUpcasterRegistration> upcasters)
    {
        foreach (var registration in registrations)
        {
            Register(registration.EventType, registration.EventTypeName);
        }

        foreach (var alias in aliases)
        {
            RegisterAlias(alias.EventType, alias.EventTypeName);
        }

        foreach (var upcaster in upcasters)
        {
            RegisterUpcaster(upcaster);
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

    internal bool TryResolveMaterializedEvent(DbEvent dbEvent, out Type eventType, out object? data)
    {
        eventType = null!;
        data = null;

        if (string.IsNullOrWhiteSpace(dbEvent.TypeName))
        {
            return false;
        }

        var eventTypeName = dbEvent.TypeName.Trim();

        if (_upcasterByName.TryGetValue(eventTypeName, out var upcaster))
        {
            eventType = upcaster.EventType;
            data = upcaster.Upcast(dbEvent, eventType);
            if (data is null)
            {
                throw new EventMaterializationException(
                    $"Upcaster '{eventTypeName}' returned null for event type '{eventType.FullName ?? eventType.Name}'.",
                    dbEvent);
            }

            return true;
        }

        if (_canonicalNameByAlias.TryGetValue(eventTypeName, out var canonicalName))
        {
            eventType = _typeByName[canonicalName];
            return true;
        }

        if (_typeByName.TryGetValue(eventTypeName, out eventType!))
        {
            return true;
        }

        return false;
    }

    internal bool TryResolveMaterializedEventType(string eventTypeName, out Type eventType)
    {
        eventType = null!;

        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            return false;
        }

        eventTypeName = eventTypeName.Trim();

        if (_upcasterByName.TryGetValue(eventTypeName, out var upcaster))
        {
            eventType = upcaster.EventType;
            return true;
        }

        if (_canonicalNameByAlias.TryGetValue(eventTypeName, out var canonicalName))
        {
            eventType = _typeByName[canonicalName];
            return true;
        }

        return _typeByName.TryGetValue(eventTypeName, out eventType!);
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

        if (_nameByAqn.TryGetValue(assemblyQualifiedName, out var mappedTypeName))
        {
            typeName = mappedTypeName;
            return true;
        }

        var fullName = EventTypeNameHelper.GetFullNameFromAqn(assemblyQualifiedName);
        if (!string.IsNullOrWhiteSpace(fullName) && _nameByFullName.TryGetValue(fullName, out mappedTypeName))
        {
            typeName = mappedTypeName;
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

    private void RegisterAlias(Type eventType, string eventTypeName)
    {
        if (!_nameByType.TryGetValue(eventType, out var canonicalName))
        {
            throw new InvalidOperationException(
                $"Event type '{eventType.FullName ?? eventType.Name}' must be registered before alias '{eventTypeName}' can be added.");
        }

        eventTypeName = NormalizeEventTypeName(eventTypeName);

        if (_typeByName.ContainsKey(eventTypeName))
        {
            throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is already registered as a canonical event type name.");
        }

        if (_upcasterByName.ContainsKey(eventTypeName))
        {
            throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is already registered as an upcaster source.");
        }

        if (_canonicalNameByAlias.TryGetValue(eventTypeName, out var existingCanonicalName)
            && !string.Equals(existingCanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Event type alias '{eventTypeName}' is already registered for '{existingCanonicalName}'.");
        }

        _canonicalNameByAlias[eventTypeName] = canonicalName;
    }

    private void RegisterUpcaster(EventUpcasterRegistration upcaster)
    {
        if (!_nameByType.ContainsKey(upcaster.EventType))
        {
            throw new InvalidOperationException(
                $"Event type '{upcaster.EventType.FullName ?? upcaster.EventType.Name}' must be registered before upcaster '{upcaster.FromEventTypeName}' can be added.");
        }

        var fromEventTypeName = NormalizeEventTypeName(upcaster.FromEventTypeName);

        if (_typeByName.ContainsKey(fromEventTypeName))
        {
            throw new InvalidOperationException(
                $"Event type name '{fromEventTypeName}' is already registered as a canonical event type name.");
        }

        if (_canonicalNameByAlias.ContainsKey(fromEventTypeName))
        {
            throw new InvalidOperationException(
                $"Event type name '{fromEventTypeName}' is already registered as an alias.");
        }

        if (_upcasterByName.TryGetValue(fromEventTypeName, out var existing))
        {
            throw new InvalidOperationException(
                $"Event type upcaster source '{fromEventTypeName}' is already registered for '{existing.EventType.FullName ?? existing.EventType.Name}'.");
        }

        _upcasterByName[fromEventTypeName] = upcaster;
    }

    private static string NormalizeEventTypeName(string eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        return eventTypeName.Trim();
    }
}

internal sealed record EventTypeRegistration(Type EventType, string EventTypeName);

internal sealed record EventTypeAliasRegistration(Type EventType, string EventTypeName);

internal sealed record EventUpcasterRegistration(
    Type EventType,
    string FromEventTypeName,
    Func<DbEvent, Type, object> Upcast);
