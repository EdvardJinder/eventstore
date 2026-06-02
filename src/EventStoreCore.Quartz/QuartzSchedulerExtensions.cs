using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace EventStoreCore.Quartz;

/// <summary>
/// Extension methods for registering Quartz-backed scheduler integrations.
/// </summary>
public static class QuartzSchedulerExtensions
{
    internal const string ProviderName = "Quartz";

    /// <summary>
    /// Configures Quartz as the scheduler provider for the current EventStore registration.
    /// Quartz itself must be configured separately by the application.
    /// </summary>
    /// <param name="builder">The scheduler builder.</param>
    /// <returns>The scheduler builder for chaining.</returns>
    public static ISchedulerBuilder UsingQuartz(this ISchedulerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSchedulerProvider(ProviderName);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISubscription, QuartzSubscription>());

        return builder;
    }

    /// <summary>
    /// Registers a Quartz action that is invoked at most once per EventStore event id for this registration.
    /// The action owns all Quartz scheduling, cancellation, trigger, and replacement semantics.
    /// Prefer the named overload for production integrations so replay identity survives type renames.
    /// </summary>
    public static IEventSchedulerBuilder<TEvent> Quartz<TEvent>(
        this IEventSchedulerBuilder<TEvent> builder,
        Func<IEvent<TEvent>, IScheduler, IServiceProvider, CancellationToken, ValueTask> action)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddQuartzAction(registrationName: null, action);
        return builder;
    }

    /// <summary>
    /// Registers a named Quartz action that is invoked at most once per EventStore event id for this registration.
    /// Use a stable name for long-lived integrations where dedupe identity must survive refactors.
    /// </summary>
    public static IEventSchedulerBuilder<TEvent> Quartz<TEvent>(
        this IEventSchedulerBuilder<TEvent> builder,
        string name,
        Func<IEvent<TEvent>, IScheduler, IServiceProvider, CancellationToken, ValueTask> action)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddQuartzAction(name, action);
        return builder;
    }

    private static void AddQuartzAction<TEvent>(
        this IServiceCollection services,
        string? registrationName,
        Func<IEvent<TEvent>, IScheduler, IServiceProvider, CancellationToken, ValueTask> action)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(action);

        services.AddOptions<SchedulerOptions>()
            .Configure(options =>
            {
                var name = string.IsNullOrWhiteSpace(registrationName)
                    ? SchedulerRegistrationName.CreateDefault(ProviderName, typeof(TEvent))
                    : SchedulerRegistrationName.Validate(registrationName);

                if (options.ContainsRegistration(ProviderName, typeof(TEvent), name))
                {
                    throw new InvalidOperationException(
                        $"A scheduler action named '{name}' is already registered for provider '{ProviderName}' and event type '{typeof(TEvent).FullName ?? typeof(TEvent).Name}'. Use an explicit unique name for each action.");
                }

                options.Registrations.Add(new QuartzEventSchedulerActionRegistration<TEvent>(name, action));
            });
    }
}
