using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Scheduling;

internal static class EventSchedulerActionRegistrationExtensions
{
    public static void AddProviderAction<TEvent, TProvider>(
        this IServiceCollection services,
        string providerName,
        string? registrationName,
        Func<IEvent<TEvent>, TProvider, IServiceProvider, CancellationToken, ValueTask> action)
        where TEvent : class
        where TProvider : notnull
    {
        ArgumentNullException.ThrowIfNull(action);

        services.AddOptions<SchedulerOptions>()
            .Configure(options =>
            {
                var name = string.IsNullOrWhiteSpace(registrationName)
                    ? SchedulerRegistrationName.CreateDefault(providerName, typeof(TEvent))
                    : SchedulerRegistrationName.Validate(registrationName);

                if (options.ContainsRegistration(providerName, typeof(TEvent), name))
                {
                    throw new InvalidOperationException(
                        $"A scheduler action named '{name}' is already registered for provider '{providerName}' and event type '{typeof(TEvent).FullName ?? typeof(TEvent).Name}'. Use an explicit unique name for each action.");
                }

                options.Registrations.Add(new EventSchedulerActionRegistration<TEvent, TProvider>(
                    providerName,
                    name,
                    action));
            });
    }
}
