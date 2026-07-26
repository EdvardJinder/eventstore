using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

internal sealed class EventTypeBuilder<TEvent>(IServiceCollection services) : IEventTypeBuilder<TEvent>
    where TEvent : class
{
    public IEventTypeBuilder<TEvent> AddAlias(string eventTypeName)
    {
        ValidateEventTypeName(eventTypeName);
        services.AddSingleton(new EventTypeAliasRegistration(typeof(TEvent), eventTypeName.Trim()));
        return this;
    }

    public IEventTypeBuilder<TEvent> AddUpcaster<TOldEvent>(
        string fromEventTypeName,
        Func<TOldEvent, TEvent> upcaster)
        where TOldEvent : class
    {
        ValidateEventTypeName(fromEventTypeName);
        ArgumentNullException.ThrowIfNull(upcaster);

        services.AddSingleton(new EventUpcasterRegistration(
            typeof(TEvent),
            fromEventTypeName.Trim(),
            (dbEvent, targetType, serializer) =>
            {
                try
                {
                    var oldEvent = serializer.Deserialize(dbEvent.Data, typeof(TOldEvent)) as TOldEvent;
                    if (oldEvent is null)
                    {
                        throw new EventMaterializationException(
                            $"Could not deserialize event data to upcast source type '{typeof(TOldEvent).FullName ?? typeof(TOldEvent).Name}'.",
                            dbEvent);
                    }

                    return upcaster(oldEvent);
                }
                catch (EventMaterializationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new EventMaterializationException(
                        $"Could not upcast event data from '{fromEventTypeName}' to '{targetType.FullName ?? targetType.Name}'.",
                        dbEvent,
                        ex);
                }
            }));

        return this;
    }

    public IEventTypeBuilder<TEvent> AddUpcaster(
        string fromEventTypeName,
        Func<JsonObject, TEvent> upcaster)
    {
        ValidateEventTypeName(fromEventTypeName);
        ArgumentNullException.ThrowIfNull(upcaster);

        services.AddSingleton(new EventUpcasterRegistration(
            typeof(TEvent),
            fromEventTypeName.Trim(),
            (dbEvent, targetType, serializer) =>
            {
                try
                {
                    var jsonNode = JsonNode.Parse(dbEvent.Data);
                    if (jsonNode is not JsonObject jsonObject)
                    {
                        throw new EventMaterializationException(
                            $"Could not deserialize event data to JSON object for upcaster '{fromEventTypeName}'.",
                            dbEvent);
                    }

                    return upcaster(jsonObject);
                }
                catch (EventMaterializationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new EventMaterializationException(
                        $"Could not upcast event data from '{fromEventTypeName}' to '{targetType.FullName ?? targetType.Name}'.",
                        dbEvent,
                        ex);
                }
            }));

        return this;
    }

    public IEventTypeBuilder<TEvent> AddUpcaster(
        int fromSchemaVersion,
        int toSchemaVersion,
        Func<string, string> upcaster)
    {
        if (fromSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromSchemaVersion));
        }

        if (toSchemaVersion <= fromSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toSchemaVersion),
                "Target schema version must be greater than the source version.");
        }

        ArgumentNullException.ThrowIfNull(upcaster);
        services.AddSingleton(new EventSchemaUpcasterRegistration(
            typeof(TEvent),
            fromSchemaVersion,
            toSchemaVersion,
            upcaster));
        return this;
    }

    private static void ValidateEventTypeName(string eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name cannot be empty.", nameof(eventTypeName));
        }
    }
}
