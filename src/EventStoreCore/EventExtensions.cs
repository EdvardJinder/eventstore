using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Globalization;
using EventStoreCore.Abstractions;

namespace EventStoreCore;

internal static class EventExtensions
{
    internal static IEvent ToEvent(this DbEvent dbEvent)
    {
        return ToEventCore(dbEvent, null, new SystemTextJsonEventStoreSerializer());
    }

    internal static IEvent ToEvent(
        this DbEvent dbEvent,
        EventTypeRegistry? registry,
        IEventStoreSerializer serializer)
    {
        return ToEventCore(dbEvent, registry, serializer);
    }

    internal static IEvent ToEvent(this DbEvent dbEvent, EventTypeRegistry? registry)
        => ToEventCore(dbEvent, registry, new SystemTextJsonEventStoreSerializer());

    private static IEvent ToEventCore(
        DbEvent dbEvent,
        EventTypeRegistry? registry,
        IEventStoreSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(dbEvent);

        if (registry is not null
            && !string.IsNullOrWhiteSpace(dbEvent.TypeName)
            && registry.TryResolveMaterializedEvent(
                dbEvent,
                serializer,
                out var registeredType,
                out var registeredData))
        {
            return CreateEvent(dbEvent, registeredType, registeredData);
        }

        var eventType = EventTypeResolver.ResolveEventType(dbEvent, registry);
        return CreateEvent(
            dbEvent,
            eventType,
            Event.Deserialize(dbEvent, eventType, serializer));
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

