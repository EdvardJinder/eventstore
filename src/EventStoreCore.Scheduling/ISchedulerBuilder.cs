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
    /// Schedules delayed work when the specified event is observed.
    /// Reprocessing the same event id for the same key is treated as a no-op.
    /// A later event using the same key replaces the previously scheduled work.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <typeparam name="TArgs">The scheduled job payload type.</typeparam>
    /// <param name="key">Selects the stable schedule key used for deduplication and replacement.</param>
    /// <param name="delay">Selects the delay before the scheduled work should run.</param>
    /// <param name="args">Builds the scheduled job payload.</param>
    /// <returns>The builder for chaining.</returns>
    ISchedulerBuilder Schedule<TEvent, TArgs>(
        Func<IEvent<TEvent>, ScheduleKey> key,
        Func<IEvent<TEvent>, TimeSpan> delay,
        Func<IEvent<TEvent>, TArgs> args)
        where TEvent : class
        where TArgs : class;

    /// <summary>
    /// Cancels the scheduled work for the selected key when the specified event is observed.
    /// Missing, replaced, or already-fired schedules are treated as a no-op.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="key">Selects the stable schedule key to cancel.</param>
    /// <returns>The builder for chaining.</returns>
    ISchedulerBuilder Cancel<TEvent>(Func<IEvent<TEvent>, ScheduleKey> key)
        where TEvent : class;
}
