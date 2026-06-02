using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace EventStoreCore.TickerQ;

/// <summary>
/// Extension methods for registering TickerQ-backed scheduler integrations.
/// </summary>
public static class TickerQSchedulerExtensions
{
    internal const string ProviderName = "TickerQ";

    /// <summary>
    /// Configures TickerQ as the scheduler provider for the current EventStore registration.
    /// TickerQ itself must be configured separately by the application.
    /// </summary>
    /// <param name="builder">The scheduler builder.</param>
    /// <returns>The scheduler builder for chaining.</returns>
    public static ISchedulerBuilder UsingTickerQ(this ISchedulerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSchedulerProvider(ProviderName);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISubscription, TickerQSubscription>());

        return builder;
    }

    /// <summary>
    /// Registers a TickerQ action that is invoked at most once per EventStore event id for this registration.
    /// The action owns all TickerQ scheduling, cancellation, and replacement semantics.
    /// Prefer the named overload for production integrations so replay identity survives type renames.
    /// </summary>
    public static IEventSchedulerBuilder<TEvent> TickerQ<TEvent>(
        this IEventSchedulerBuilder<TEvent> builder,
        Func<IEvent<TEvent>, ITimeTickerManager<TimeTickerEntity>, IServiceProvider, CancellationToken, ValueTask> action)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddProviderAction(ProviderName, registrationName: null, action);
        return builder;
    }

    /// <summary>
    /// Registers a named TickerQ action that is invoked at most once per EventStore event id for this registration.
    /// Use a stable name for long-lived integrations where dedupe identity must survive refactors.
    /// </summary>
    public static IEventSchedulerBuilder<TEvent> TickerQ<TEvent>(
        this IEventSchedulerBuilder<TEvent> builder,
        string name,
        Func<IEvent<TEvent>, ITimeTickerManager<TimeTickerEntity>, IServiceProvider, CancellationToken, ValueTask> action)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddProviderAction(ProviderName, name, action);
        return builder;
    }
}
