using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore.Scheduling;

/// <summary>
/// Extension methods for registering scheduler providers in the current service collection.
/// </summary>
public static class SchedulerProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scheduler provider selected for the current EventStore service collection.
    /// Re-registering the same provider is treated as a no-op.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="providerName">The unique scheduler provider name.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a different scheduler provider has already been registered for the same service collection.
    /// </exception>
    public static IServiceCollection AddSchedulerProvider(
        this IServiceCollection services,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Scheduler provider names must not be null, empty, or whitespace.", nameof(providerName));
        }

        var existingRegistration = services
            .LastOrDefault(d => d.ServiceType == typeof(SchedulerProviderRegistration))
            ?.ImplementationInstance as SchedulerProviderRegistration;

        if (existingRegistration is not null)
        {
            if (!string.Equals(existingRegistration.ProviderName, providerName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"A scheduler provider is already registered for this service collection ('{existingRegistration.ProviderName}'). Only one scheduler provider can be configured.");
            }

            return services;
        }

        services.TryAddSingleton(new SchedulerProviderRegistration(providerName));
        return services;
    }
}
