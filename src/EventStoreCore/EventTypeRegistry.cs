namespace EventStoreCore;

internal sealed class EventTypeRegistry
{
    private readonly Dictionary<Type, string> _nameByType = new();
    private readonly Dictionary<Type, int> _schemaVersionByType = new();
    private readonly Dictionary<string, Type> _typeByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _canonicalNameByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventUpcasterRegistration> _upcasterByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(Type EventType, int FromVersion), EventSchemaUpcasterRegistration>
        _schemaUpcasterByVersion = new();
    private readonly Dictionary<string, string> _nameByAqn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameByFullName = new(StringComparer.Ordinal);

    internal EventTypeRegistry(IEnumerable<EventTypeRegistration> registrations)
        : this(registrations, [], [], [])
    {
    }

    internal EventTypeRegistry(
        IEnumerable<EventTypeRegistration> registrations,
        IEnumerable<EventTypeAliasRegistration> aliases,
        IEnumerable<EventUpcasterRegistration> upcasters)
        : this(registrations, aliases, upcasters, [])
    {
    }

    internal EventTypeRegistry(
        IEnumerable<EventTypeRegistration> registrations,
        IEnumerable<EventTypeAliasRegistration> aliases,
        IEnumerable<EventUpcasterRegistration> upcasters,
        IEnumerable<EventSchemaUpcasterRegistration> schemaUpcasters)
    {
        foreach (var registration in registrations)
        {
            Register(registration.EventType, registration.EventTypeName, registration.SchemaVersion);
        }

        foreach (var alias in aliases)
        {
            RegisterAlias(alias.EventType, alias.EventTypeName);
        }

        foreach (var upcaster in upcasters)
        {
            RegisterUpcaster(upcaster);
        }

        foreach (var upcaster in schemaUpcasters)
        {
            RegisterSchemaUpcaster(upcaster);
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

    internal bool TryResolveMaterializedEvent(
        DbEvent dbEvent,
        EventStoreCore.Abstractions.IEventStoreSerializer serializer,
        out Type eventType,
        out object? data)
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
            data = upcaster.Upcast(dbEvent, eventType, serializer);
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
            data = UpcastSchema(dbEvent, eventType, serializer);
            return true;
        }

        if (_typeByName.TryGetValue(eventTypeName, out eventType!))
        {
            data = UpcastSchema(dbEvent, eventType, serializer);
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

    internal int ResolveSchemaVersion(Type eventType)
        => _schemaVersionByType.TryGetValue(eventType, out var schemaVersion)
            ? schemaVersion
            : 1;

    private void Register(Type eventType, string eventTypeName, int schemaVersion)
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
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be greater than zero.");
        }

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
        _schemaVersionByType[eventType] = schemaVersion;
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

    private void RegisterSchemaUpcaster(EventSchemaUpcasterRegistration upcaster)
    {
        if (!_schemaVersionByType.TryGetValue(upcaster.EventType, out var currentVersion))
        {
            throw new InvalidOperationException(
                $"Event type '{upcaster.EventType.FullName ?? upcaster.EventType.Name}' must be registered before schema upcasters are added.");
        }

        if (upcaster.ToVersion > currentVersion)
        {
            throw new InvalidOperationException(
                $"Schema upcaster target version {upcaster.ToVersion} exceeds current schema version {currentVersion}.");
        }

        if (!_schemaUpcasterByVersion.TryAdd((upcaster.EventType, upcaster.FromVersion), upcaster))
        {
            throw new InvalidOperationException(
                $"A schema upcaster from version {upcaster.FromVersion} is already registered for '{upcaster.EventType.FullName ?? upcaster.EventType.Name}'.");
        }
    }

    private object? UpcastSchema(
        DbEvent dbEvent,
        Type eventType,
        EventStoreCore.Abstractions.IEventStoreSerializer serializer)
    {
        var storedVersion = dbEvent.SchemaVersion <= 0 ? 1 : dbEvent.SchemaVersion;
        var currentVersion = ResolveSchemaVersion(eventType);
        if (storedVersion == currentVersion)
        {
            return Event.Deserialize(dbEvent, eventType, serializer);
        }

        if (storedVersion > currentVersion)
        {
            throw new EventMaterializationException(
                $"Stored schema version {storedVersion} is newer than configured version {currentVersion} for '{dbEvent.TypeName}'.",
                dbEvent);
        }

        var data = dbEvent.Data;
        var version = storedVersion;
        while (version < currentVersion)
        {
            if (!_schemaUpcasterByVersion.TryGetValue((eventType, version), out var upcaster))
            {
                throw new EventMaterializationException(
                    $"No schema upcaster is registered from version {version} to {currentVersion} for '{dbEvent.TypeName}'.",
                    dbEvent);
            }

            try
            {
                data = upcaster.Upcast(data)
                    ?? throw new InvalidOperationException("Schema upcaster returned null.");
                version = upcaster.ToVersion;
            }
            catch (Exception ex) when (ex is not EventMaterializationException)
            {
                throw new EventMaterializationException(
                    $"Could not upcast '{dbEvent.TypeName}' from schema version {upcaster.FromVersion} to {upcaster.ToVersion}.",
                    dbEvent,
                    ex);
            }
        }

        return Event.Deserialize(dbEvent, eventType, serializer, data);
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

internal sealed record EventTypeRegistration(
    Type EventType,
    string EventTypeName,
    int SchemaVersion = 1);

internal sealed record EventTypeAliasRegistration(Type EventType, string EventTypeName);

internal sealed record EventUpcasterRegistration(
    Type EventType,
    string FromEventTypeName,
    Func<DbEvent, Type, EventStoreCore.Abstractions.IEventStoreSerializer, object> Upcast);

internal sealed record EventSchemaUpcasterRegistration(
    Type EventType,
    int FromVersion,
    int ToVersion,
    Func<string, string> Upcast);
