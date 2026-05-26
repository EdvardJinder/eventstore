using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore.Hangfire;

/// <summary>
/// Extension methods for registering Hangfire-backed scheduler integrations.
/// </summary>
public static class HangfireSchedulerExtensions
{
    /// <summary>
    /// Configures Hangfire as the scheduler provider for the current EventStore registration.
    /// Hangfire itself must be configured separately by the application.
    /// </summary>
    /// <param name="builder">The scheduler builder.</param>
    /// <returns>The scheduler builder for chaining.</returns>
    public static ISchedulerBuilder UsingHangfire(this ISchedulerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSchedulerProvider("Hangfire");

        builder.Services.TryAddSingleton<HangfireScheduleRegistry>();
        builder.Services.TryAddSingleton<ISchedulerExecutionAdapter, HangfireScheduleService>();
        builder.Services.TryAddTransient(typeof(HangfireScheduledJob<>));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISubscription, HangfireSubscription>());

        return builder;
    }
}
