using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerQ.DependencyInjection;

namespace EventStoreCore.TickerQ;

/// <summary>
/// Extension methods for registering TickerQ-backed scheduler integrations.
/// </summary>
public static class TickerQSchedulerExtensions
{
    /// <summary>
    /// Configures TickerQ as the scheduler provider for the current EventStore registration.
    /// TickerQ itself must be configured separately by the application.
    /// </summary>
    /// <param name="builder">The scheduler builder.</param>
    /// <returns>The scheduler builder for chaining.</returns>
    public static ISchedulerBuilder UsingTickerQ(this ISchedulerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSchedulerProvider("TickerQ");
        builder.Services.TryAddSingleton<ISchedulerExecutionAdapter, TickerQScheduleService>();
        builder.Services.TryAddTransient<TickerQScheduledJobDispatcher>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISubscription, TickerQSubscription>());

        if (!builder.Services.Any(d => d.ServiceType == typeof(TickerQRegistrationMarker)))
        {
            builder.Services.AddSingleton<TickerQRegistrationMarker>();
            builder.Services.MapTicker(
                TickerQConstants.FunctionName,
                static async (context, serviceProvider, cancellationToken) =>
                {
                    var dispatcher = serviceProvider.GetRequiredService<TickerQScheduledJobDispatcher>();
                    await dispatcher.ExecuteAsync(context, cancellationToken);
                });
        }

        return builder;
    }
}
