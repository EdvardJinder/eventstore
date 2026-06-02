using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Scheduling;

/// <summary>
/// Builder for configuring scheduler integrations for EventStore.
/// </summary>
public interface ISchedulerBuilder
{
    /// <summary>
    /// Gets the underlying service collection so scheduler provider extensions can register their services.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Begins configuring scheduler actions for the specified event type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <returns>The event scheduler builder.</returns>
    IEventSchedulerBuilder<TEvent> On<TEvent>()
        where TEvent : class;
}

/// <summary>
/// Builder for provider-native scheduler actions triggered by a specific EventStore event type.
/// </summary>
/// <typeparam name="TEvent">The event payload type.</typeparam>
public interface IEventSchedulerBuilder<TEvent>
    where TEvent : class
{
    /// <summary>
    /// Gets the underlying service collection so scheduler provider extensions can register their services.
    /// </summary>
    IServiceCollection Services { get; }
}
