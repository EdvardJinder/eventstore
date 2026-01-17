using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

/// <summary>
/// Builder for configuring event store services and providers.
/// </summary>
public interface IEventStoreBuilder
{
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The registered provider instance, if any.
    /// </summary>
    object? Provider { get; }

    /// <summary>
    /// Registers the provider instance used by builder extensions.
    /// </summary>
    /// <param name="provider">The provider instance.</param>
    void UseProvider(object provider);

    /// <summary>
    /// Registers a subscription type for event processing.
    /// </summary>
    /// <typeparam name="TSubscription">The subscription implementation.</typeparam>
    /// <returns>The builder for chaining.</returns>
    IEventStoreBuilder AddSubscription<TSubscription>()
        where TSubscription : ISubscription;
}


