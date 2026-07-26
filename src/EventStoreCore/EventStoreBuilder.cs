using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore;

internal sealed class EventStoreBuilder(
    IServiceCollection services
    ) : IEventStoreBuilder
{
    private readonly HashSet<string> _subscriptionNames = new(StringComparer.Ordinal);

    public IServiceCollection Services => services;
    public object? Provider { get; private set; }
    public void UseProvider(object provider)
    {
        Provider = provider;
    }

    public IEventStoreBuilder AddSubscription<TSubscription>() where TSubscription : class, ISubscription
    {
        return AddSubscription<TSubscription>(_ => { });
    }

    public IEventStoreBuilder AddSubscription<TSubscription>(
        Action<SubscriptionRegistrationOptions> configure)
        where TSubscription : class, ISubscription
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new SubscriptionRegistrationOptions();
        configure(options);
        var name = ResolveSubscriptionName<TSubscription>(options);

        services.TryAddSingleton(typeof(TSubscription));
        services.AddSingleton(typeof(ISubscription), sp => sp.GetRequiredService<TSubscription>());
        services.AddSingleton(sp => new SubscriptionRegistration
        {
            Name = name,
            Subscription = sp.GetRequiredService<TSubscription>(),
            Options = options
        });
        return this;
    }

    public IEventStoreBuilder AddSubscription<TSubscription, TEvent>(
        Action<SubscriptionRegistrationOptions>? configure = null)
        where TSubscription : class, ISubscription<TEvent>
        where TEvent : class
    {
        var options = new SubscriptionRegistrationOptions();
        configure?.Invoke(options);
        options.IncludeEventType(typeof(TEvent));
        var name = ResolveSubscriptionName<TSubscription>(options);

        services.TryAddSingleton<TSubscription>();
        services.AddSingleton<ISubscription>(sp =>
            new TypedSubscriptionAdapter<TSubscription, TEvent>(sp.GetRequiredService<TSubscription>()));
        services.AddSingleton(sp => new SubscriptionRegistration
        {
            Name = name,
            Subscription = new TypedSubscriptionAdapter<TSubscription, TEvent>(sp.GetRequiredService<TSubscription>()),
            Options = options
        });
        return this;
    }

    private string ResolveSubscriptionName<TSubscription>(SubscriptionRegistrationOptions options)
    {
        var name = string.IsNullOrWhiteSpace(options.Name)
            ? typeof(TSubscription).AssemblyQualifiedName!
            : options.Name;

        if (!_subscriptionNames.Add(name))
        {
            throw new InvalidOperationException($"A subscription with logical name '{name}' is already registered.");
        }

        return name;
    }
}
