using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EventStoreCore;

/// <summary>
/// Registers standalone EF entity-outbox capture, readers, subscriptions, and dispatch.
/// </summary>
public static class EntityOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Adds entity change capture and an <see cref="IOutboxReader" /> for an existing EF context.
    /// </summary>
    /// <typeparam name="TDbContext">The context containing the application entities and outbox tables.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The entity capture configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEntityOutbox<TDbContext>(
        this IServiceCollection services,
        Action<IEntityOutboxBuilder> configure)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EntityOutboxBuilder<TDbContext>(services);
        configure(builder);
        var registry = builder.Build();

        services.AddSingleton(registry);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new EventTypeRegistry(
            sp.GetServices<EventTypeRegistration>(),
            sp.GetServices<EventTypeAliasRegistration>(),
            sp.GetServices<EventUpcasterRegistration>()));

        services.AddDbContext<TDbContext>((sp, options) =>
        {
            options.AddInterceptors(new EntityOutboxInterceptor<TDbContext>(
                sp.GetRequiredService<EntityOutboxRegistry<TDbContext>>(),
                sp.GetRequiredService<EventTypeRegistry>(),
                sp.GetRequiredService<TimeProvider>()));
        });

        services.TryAddScoped<IOutboxReader, EntityOutboxReader<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Registers a custom outbox subscription.
    /// </summary>
    /// <typeparam name="TSubscription">The outbox subscription implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxSubscription<TSubscription>(this IServiceCollection services)
        where TSubscription : class, IOutboxSubscription
    {
        return services.AddOutboxSubscription<TSubscription>(_ => { });
    }

    /// <summary>
    /// Registers a custom outbox subscription with a stable logical identity.
    /// </summary>
    /// <typeparam name="TSubscription">The outbox subscription implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The subscription registration configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxSubscription<TSubscription>(
        this IServiceCollection services,
        Action<OutboxSubscriptionRegistrationOptions> configure)
        where TSubscription : class, IOutboxSubscription
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OutboxSubscriptionRegistrationOptions();
        configure(options);
        var name = string.IsNullOrWhiteSpace(options.Name)
            ? typeof(TSubscription).AssemblyQualifiedName!
            : options.Name.Trim();
        EnsureRegistrationIsUnique(services, name, typeof(TSubscription));

        services.TryAddScoped<TSubscription>();
        services.AddScoped<IOutboxSubscription>(sp => sp.GetRequiredService<TSubscription>());
        services.AddSingleton(new OutboxSubscriptionRegistration(
            name,
            typeof(TSubscription),
            options,
            sp => sp.GetRequiredService<TSubscription>()));
        return services;
    }

    /// <summary>
    /// Registers a strongly typed custom outbox subscription.
    /// </summary>
    /// <typeparam name="TSubscription">The outbox subscription implementation.</typeparam>
    /// <typeparam name="TEvent">The handled outbox event payload type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional subscription registration configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxSubscription<TSubscription, TEvent>(
        this IServiceCollection services,
        Action<OutboxSubscriptionRegistrationOptions>? configure = null)
        where TSubscription : class, IOutboxSubscription<TEvent>
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OutboxSubscriptionRegistrationOptions();
        configure?.Invoke(options);
        options.IncludeEventType(typeof(TEvent));
        var name = string.IsNullOrWhiteSpace(options.Name)
            ? typeof(TSubscription).AssemblyQualifiedName!
            : options.Name.Trim();
        EnsureRegistrationIsUnique(services, name, typeof(TSubscription));

        services.TryAddScoped<TSubscription>();
        services.AddScoped<IOutboxSubscription>(sp =>
            new TypedOutboxSubscriptionAdapter<TSubscription, TEvent>(
                sp.GetRequiredService<TSubscription>()));
        services.AddSingleton(new OutboxSubscriptionRegistration(
            name,
            typeof(TSubscription),
            options,
            sp => new TypedOutboxSubscriptionAdapter<TSubscription, TEvent>(
                sp.GetRequiredService<TSubscription>())));
        return services;
    }

    /// <summary>
    /// Adds the hosted outbox dispatcher using the registered distributed lock provider.
    /// </summary>
    /// <typeparam name="TDbContext">The context containing the outbox tables.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional daemon configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEntityOutboxDaemon<TDbContext>(
        this IServiceCollection services,
        Action<EntityOutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        return services.AddEntityOutboxDaemon<TDbContext>(
            sp => sp.GetRequiredService<IDistributedLockProvider>(),
            configure);
    }

    /// <summary>
    /// Adds the hosted outbox dispatcher using a custom distributed lock provider.
    /// </summary>
    /// <typeparam name="TDbContext">The context containing the outbox tables.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lockProviderFactory">Creates the distributed lock provider.</param>
    /// <param name="configure">Optional daemon configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEntityOutboxDaemon<TDbContext>(
        this IServiceCollection services,
        Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory,
        Action<EntityOutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(lockProviderFactory);

        services.TryAddSingleton(lockProviderFactory);
        services.TryAddSingleton<DaemonHealthMonitor>();
        services.AddOptions<EntityOutboxOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(
                options => options.MaxConcurrentWorkers > 0,
                $"{nameof(EntityOutboxOptions.MaxConcurrentWorkers)} must be positive.")
            .Validate(
                options =>
                    options.LockTimeout >= TimeSpan.Zero ||
                    options.LockTimeout == Timeout.InfiniteTimeSpan,
                $"{nameof(EntityOutboxOptions.LockTimeout)} must be non-negative or Timeout.InfiniteTimeSpan.")
            .Validate(
                options => options.BatchSize > 0,
                $"{nameof(EntityOutboxOptions.BatchSize)} must be positive.")
            .Validate(
                options => options.MaxRetryAttempts > 0,
                $"{nameof(EntityOutboxOptions.MaxRetryAttempts)} must be positive.")
            .Validate(
                options => options.PollingInterval >= TimeSpan.Zero,
                $"{nameof(EntityOutboxOptions.PollingInterval)} must be non-negative.")
            .Validate(
                options => options.RetryDelay >= TimeSpan.Zero,
                $"{nameof(EntityOutboxOptions.RetryDelay)} must be non-negative.")
            .ValidateOnStart();
        services.TryAddSingleton<EntityOutboxDaemon<TDbContext>>();
        services.TryAddScoped<IOutboxSubscriptionManager>(sp =>
            new EntityOutboxManager<TDbContext>(
                sp.GetRequiredService<TDbContext>(),
                sp.GetRequiredService<IDistributedLockProvider>(),
                sp.GetServices<OutboxSubscriptionRegistration>(),
                sp.GetRequiredService<ILogger<EntityOutboxManager<TDbContext>>>()));
        services.AddHostedService(sp => sp.GetRequiredService<EntityOutboxDaemon<TDbContext>>());
        return services;
    }

    private static void EnsureRegistrationIsUnique(
        IServiceCollection services,
        string name,
        Type subscriptionType)
    {
        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(OutboxSubscriptionRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<OutboxSubscriptionRegistration>()
            .ToArray();
        if (registrations.Any(registration =>
                string.Equals(registration.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"An outbox subscription with logical name '{name}' is already registered.");
        }

        if (registrations.Any(registration =>
                registration.SubscriptionType == subscriptionType))
        {
            throw new InvalidOperationException(
                $"Outbox subscription type '{subscriptionType}' is already registered.");
        }
    }
}
