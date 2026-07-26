using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore;

/// <summary>
/// Extension methods for registering event type names.
/// </summary>
public static class EventStoreBuilderEventTypeExtensions
{
    /// <summary>
    /// Registers an event type using the default snake_case name.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IEventStoreBuilder AddEvent<TEvent>(this IEventStoreBuilder builder)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        var eventType = typeof(TEvent);
        var typeName = EventTypeNameHelper.ToSnakeCase(eventType);
        RegisterEvent(builder, eventType, typeName, 1);
        return builder;
    }

    /// <summary>
    /// Registers an event type with a custom logical name.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="eventTypeName">The custom event type name.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is null or whitespace.</exception>
    public static IEventStoreBuilder AddEvent<TEvent>(this IEventStoreBuilder builder, string eventTypeName)
        where TEvent : class
    {
        return AddEvent<TEvent>(builder, eventTypeName, null);
    }

    /// <summary>
    /// Registers an event type with a custom logical name and optional aliases or upcasters.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="builder">The event store builder.</param>
    /// <param name="eventTypeName">The custom event type name.</param>
    /// <param name="configure">Configures aliases and upcasters for the event type.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is null or whitespace.</exception>
    public static IEventStoreBuilder AddEvent<TEvent>(
        this IEventStoreBuilder builder,
        string eventTypeName,
        Action<IEventTypeBuilder<TEvent>>? configure)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        RegisterEvent(builder, typeof(TEvent), eventTypeName.Trim(), 1);
        configure?.Invoke(new EventTypeBuilder<TEvent>(builder.Services));
        return builder;
    }

    /// <summary>
    /// Registers the current schema version for a logical event type and configures its compatibility chain.
    /// </summary>
    /// <typeparam name="TEvent">The current event payload type.</typeparam>
    /// <param name="builder">The event-store builder.</param>
    /// <param name="eventTypeName">The stable logical event type name.</param>
    /// <param name="schemaVersion">The schema version written for new events.</param>
    /// <param name="configure">Configures aliases and deterministic upcaster steps.</param>
    /// <returns>The builder for chaining.</returns>
    public static IEventStoreBuilder AddEvent<TEvent>(
        this IEventStoreBuilder builder,
        string eventTypeName,
        int schemaVersion,
        Action<IEventTypeBuilder<TEvent>>? configure = null)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        RegisterEvent(builder, typeof(TEvent), eventTypeName.Trim(), schemaVersion);
        configure?.Invoke(new EventTypeBuilder<TEvent>(builder.Services));
        return builder;
    }

    private static void RegisterEvent(
        IEventStoreBuilder builder,
        Type eventType,
        string eventTypeName,
        int schemaVersion)
    {
        builder.Services.TryAddSingleton(sp => new EventTypeRegistry(
            sp.GetServices<EventTypeRegistration>(),
            sp.GetServices<EventTypeAliasRegistration>(),
            sp.GetServices<EventUpcasterRegistration>(),
            sp.GetServices<EventSchemaUpcasterRegistration>()));
        builder.Services.AddSingleton(new EventTypeRegistration(eventType, eventTypeName, schemaVersion));
    }
}
