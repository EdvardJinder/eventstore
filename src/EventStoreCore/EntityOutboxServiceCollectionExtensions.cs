using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<TSubscription>();
        services.AddSingleton<IOutboxSubscription>(sp => sp.GetRequiredService<TSubscription>());
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
        services.Configure<EntityOutboxOptions>(options => configure?.Invoke(options));
        services.TryAddSingleton<EntityOutboxDaemon<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<EntityOutboxDaemon<TDbContext>>());
        return services;
    }
}
