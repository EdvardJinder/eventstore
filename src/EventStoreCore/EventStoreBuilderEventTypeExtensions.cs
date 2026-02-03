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
        RegisterEvent(builder, eventType, typeName);
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
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }

        RegisterEvent(builder, typeof(TEvent), eventTypeName.Trim());
        return builder;
    }

    private static void RegisterEvent(IEventStoreBuilder builder, Type eventType, string eventTypeName)
    {
        builder.Services.TryAddSingleton(sp => new EventTypeRegistry(sp.GetServices<EventTypeRegistration>()));
        builder.Services.AddSingleton(new EventTypeRegistration(eventType, eventTypeName));
    }
}
