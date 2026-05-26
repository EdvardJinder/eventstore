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
    /// <summary>
    /// Configures Quartz as the scheduler provider for the current EventStore registration.
    /// Quartz itself must be configured separately by the application.
    /// </summary>
    /// <param name="builder">The scheduler builder.</param>
    /// <returns>The scheduler builder for chaining.</returns>
    public static ISchedulerBuilder UsingQuartz(this ISchedulerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSchedulerProvider("Quartz");

        builder.Services.TryAddSingleton<ISchedulerExecutionAdapter, QuartzScheduleService>();
        builder.Services.TryAddTransient(typeof(QuartzScheduledJob<>));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISubscription, QuartzSubscription>());

        return builder;
    }
}
