using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore;

/// <summary>
/// Extension methods for configuring scheduler integrations.
/// </summary>
public static class EventStoreBuilderSchedulingExtensions
{
    /// <summary>
    /// Adds a scheduler integration entry point to the event store builder.
    /// </summary>
    /// <param name="builder">The event store builder.</param>
    /// <param name="configure">The scheduler provider configuration.</param>
    /// <returns>The event store builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no scheduler provider is selected.
    /// </exception>
    public static IEventStoreBuilder AddScheduler(
        this IEventStoreBuilder builder,
        Action<ISchedulerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.TryAddSingleton<ISchedulerEventApplicationStore, InMemorySchedulerEventApplicationStore>();

        var schedulerBuilder = new SchedulerBuilder(builder.Services);
        configure(schedulerBuilder);

        if (!builder.Services.Any(d => d.ServiceType == typeof(SchedulerProviderRegistration)))
        {
            throw new InvalidOperationException("No scheduler provider is registered. Call UsingX(...) before or inside AddScheduler(...).");
        }

        return builder;
    }
}
