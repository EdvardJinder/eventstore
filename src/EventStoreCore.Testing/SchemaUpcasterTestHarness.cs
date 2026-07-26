using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Testing;

/// <summary>
/// Exercises a schema-version upcaster chain through the same event materialization
/// pipeline used by EventStoreCore.
/// </summary>
/// <typeparam name="TEvent">The current event payload type.</typeparam>
public sealed class SchemaUpcasterTestHarness<TEvent>
    where TEvent : class
{
    private readonly EventTypeRegistry _registry;
    private readonly IEventStoreSerializer _serializer;
    private readonly string _eventTypeName;

    /// <summary>
    /// Creates a harness that uses the default System.Text.Json event-store serializer.
    /// </summary>
    /// <param name="eventTypeName">The stable logical event type name.</param>
    /// <param name="currentSchemaVersion">The schema version used by the current event payload.</param>
    /// <param name="configure">Configures the schema upcaster chain to exercise.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="eventTypeName"/> is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="currentSchemaVersion"/> is not greater than zero.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public SchemaUpcasterTestHarness(
        string eventTypeName,
        int currentSchemaVersion,
        Action<IEventTypeBuilder<TEvent>> configure)
        : this(
            eventTypeName,
            currentSchemaVersion,
            configure,
            new SystemTextJsonEventStoreSerializer())
    {
    }

    /// <summary>
    /// Creates a harness that uses the supplied event-store serializer.
    /// </summary>
    /// <param name="eventTypeName">The stable logical event type name.</param>
    /// <param name="currentSchemaVersion">The schema version used by the current event payload.</param>
    /// <param name="configure">Configures the schema upcaster chain to exercise.</param>
    /// <param name="serializer">The serializer used to materialize the upcast payload.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="eventTypeName"/> is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="currentSchemaVersion"/> is not greater than zero.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> or <paramref name="serializer"/> is
    /// <see langword="null"/>.
    /// </exception>
    public SchemaUpcasterTestHarness(
        string eventTypeName,
        int currentSchemaVersion,
        Action<IEventTypeBuilder<TEvent>> configure,
        IEventStoreSerializer serializer)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        if (currentSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentSchemaVersion),
                currentSchemaVersion,
                "Current schema version must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(serializer);

        _eventTypeName = eventTypeName.Trim();
        _serializer = serializer;

        var services = new ServiceCollection();
        services.AddEventStore(builder => builder.AddEvent(
            _eventTypeName,
            currentSchemaVersion,
            configure));

        using var provider = services.BuildServiceProvider();
        _registry = provider.GetRequiredService<EventTypeRegistry>();
    }

    /// <summary>
    /// Upcasts and materializes a persisted JSON payload.
    /// </summary>
    /// <param name="json">The persisted JSON representation.</param>
    /// <param name="storedSchemaVersion">The schema version associated with the persisted representation.</param>
    /// <returns>The current event payload produced by the configured upcaster chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="storedSchemaVersion"/> is not greater than zero.
    /// </exception>
    /// <exception cref="EventMaterializationException">
    /// Thrown when the configured chain cannot upcast or materialize the persisted payload.
    /// </exception>
    public TEvent Upcast(string json, int storedSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (storedSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(storedSchemaVersion),
                storedSchemaVersion,
                "Stored schema version must be greater than zero.");
        }

        var storedEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            Type = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name,
            TypeName = _eventTypeName,
            SchemaVersion = storedSchemaVersion,
            Data = json,
            Version = 1
        };

        return (TEvent)storedEvent.ToEvent(_registry, _serializer).Data;
    }
}
