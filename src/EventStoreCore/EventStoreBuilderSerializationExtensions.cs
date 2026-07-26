using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace EventStoreCore;

/// <summary>
/// Extension methods for configuring event and snapshot serialization.
/// </summary>
public static class EventStoreBuilderSerializationExtensions
{
    /// <summary>
    /// Replaces the serializer used for event and snapshot payloads.
    /// </summary>
    /// <typeparam name="TSerializer">The serializer implementation type.</typeparam>
    /// <param name="builder">The event-store builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IEventStoreBuilder UseSerializer<TSerializer>(this IEventStoreBuilder builder)
        where TSerializer : class, IEventStoreSerializer
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IEventStoreSerializer, TSerializer>();
        return builder;
    }

    /// <summary>
    /// Replaces the serializer used for event and snapshot payloads.
    /// </summary>
    /// <param name="builder">The event-store builder.</param>
    /// <param name="serializer">The serializer instance.</param>
    /// <returns>The builder for chaining.</returns>
    public static IEventStoreBuilder UseSerializer(
        this IEventStoreBuilder builder,
        IEventStoreSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serializer);
        builder.Services.AddSingleton(serializer);
        return builder;
    }

    /// <summary>
    /// Configures the default System.Text.Json serializer.
    /// </summary>
    /// <param name="builder">The event-store builder.</param>
    /// <param name="configure">A callback that configures JSON options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IEventStoreBuilder UseSystemTextJson(
        this IEventStoreBuilder builder,
        Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JsonSerializerOptions();
        configure(options);
        builder.Services.AddSingleton<IEventStoreSerializer>(
            new SystemTextJsonEventStoreSerializer(options));
        return builder;
    }
}
