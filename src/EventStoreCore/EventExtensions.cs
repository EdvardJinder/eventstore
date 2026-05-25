using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Globalization;
using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Extension helpers for translating persisted events.
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Converts a <see cref="DbEvent" /> record into a runtime <see cref="IEvent" /> instance.
    /// </summary>
    /// <param name="dbEvent">The persisted event record.</param>
    /// <returns>The deserialized event wrapper.</returns>
    /// <exception cref="EventMaterializationException">Thrown when the event type or payload cannot be loaded.</exception>
    public static IEvent ToEvent(this DbEvent dbEvent)
    {
        return ToEventCore(dbEvent, null);
    }

    internal static IEvent ToEvent(this DbEvent dbEvent, EventTypeRegistry? registry)
    {
        return ToEventCore(dbEvent, registry);
    }

    private static IEvent ToEventCore(DbEvent dbEvent, EventTypeRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(dbEvent);

        if (registry is not null
            && !string.IsNullOrWhiteSpace(dbEvent.TypeName)
            && registry.TryResolveMaterializedEvent(dbEvent, out var registeredType, out var registeredData))
        {
            return CreateEvent(dbEvent, registeredType, registeredData);
        }

        var eventType = EventTypeResolver.ResolveEventType(dbEvent, registry);
        return CreateEvent(dbEvent, eventType, null);
    }

    private static IEvent CreateEvent(DbEvent dbEvent, Type eventType, object? data)
    {
        var eventInstanceType = typeof(Event<>).MakeGenericType(eventType);

        try
        {
            var eventInstance = data is null
                ? Activator.CreateInstance(eventInstanceType, dbEvent, eventType)
                : Activator.CreateInstance(
                    eventInstanceType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: [dbEvent, eventType, data],
                    culture: CultureInfo.InvariantCulture);
            if (eventInstance is null)
            {
                throw new EventMaterializationException(
                    $"Could not create instance of event type '{eventInstanceType}'.",
                    dbEvent);
            }

            return (IEvent)eventInstance;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EventMaterializationException inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw; // Required for compiler control flow analysis
        }
        catch (Exception ex)
        {
            throw new EventMaterializationException(
                $"Could not create instance of event type '{eventInstanceType}'.",
                dbEvent,
                ex);
        }
    }
}

