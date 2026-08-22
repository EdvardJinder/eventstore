using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

/// <summary>
/// Registers module-local event handlers that participate in an EF Core save operation.
/// </summary>
public static class InlineEventHandlerServiceCollectionExtensions
{
    /// <summary>
    /// Registers inline event handlers for the specified context and adds their save interceptor.
    /// </summary>
    /// <typeparam name="TDbContext">The context whose save operation dispatches the handlers.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures handlers, ordering, source filters, and the dispatch limit.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInlineEventHandlers<TDbContext>(
        this IServiceCollection services,
        Action<IInlineEventHandlerBuilder> configure)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(InlineEventHandlerRegistrationMarker<TDbContext>)))
        {
            throw new InvalidOperationException(
                $"Inline event handlers are already configured for DbContext '{typeof(TDbContext).FullName}'.");
        }

        var builder = new InlineEventHandlerBuilder(services);
        configure(builder);
        var configuration = builder.Build<TDbContext>();

        services.AddSingleton(new InlineEventHandlerRegistrationMarker<TDbContext>());
        services.AddSingleton(configuration);
        services.AddDbContext<TDbContext>((serviceProvider, options) =>
        {
            options.AddInterceptors(new InlineEventHandlerInterceptor<TDbContext>(
                serviceProvider,
                serviceProvider.GetRequiredService<InlineEventHandlerConfiguration<TDbContext>>(),
                serviceProvider.GetService<EntityOutboxCapture<TDbContext>>(),
                serviceProvider.GetService<EventTypeRegistry>(),
                serviceProvider.GetService<IEventStoreSerializer>()));
        });

        return services;
    }
}

internal sealed class InlineEventHandlerBuilder(IServiceCollection services) : IInlineEventHandlerBuilder
{
    private readonly List<InlineEventHandlerRegistration> _registrations = [];

    public int MaxDispatchCount { get; set; } = 1_000;

    public IInlineEventHandlerBuilder Add<THandler, TEvent>(
        Action<InlineEventHandlerRegistrationOptions>? configure = null)
        where THandler : class, IInlineEventHandler<TEvent>
        where TEvent : class
    {
        var options = new InlineEventHandlerRegistrationOptions();
        configure?.Invoke(options);
        ValidateSources(options.Sources);

        if (_registrations.Any(registration =>
                registration.HandlerType == typeof(THandler) &&
                registration.EventType == typeof(TEvent) &&
                (registration.Sources & options.Sources) != 0))
        {
            throw new InvalidOperationException(
                $"Inline handler '{typeof(THandler).FullName}' is already registered for event " +
                $"'{typeof(TEvent).FullName}' with an overlapping source selection.");
        }

        var existingDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(THandler))
            .ToArray();
        if (existingDescriptors.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped))
        {
            throw new InvalidOperationException(
                $"Inline handler '{typeof(THandler).FullName}' must be registered with scoped lifetime.");
        }

        if (existingDescriptors.Length == 0)
        {
            services.AddScoped<THandler>();
        }

        _registrations.Add(new InlineEventHandlerRegistration<THandler, TEvent>(
            options.Order,
            options.Sources,
            _registrations.Count));
        return this;
    }

    internal InlineEventHandlerConfiguration<TDbContext> Build<TDbContext>()
        where TDbContext : DbContext
    {
        if (MaxDispatchCount <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxDispatchCount)} must be greater than zero.");
        }

        return new InlineEventHandlerConfiguration<TDbContext>(
            MaxDispatchCount,
            _registrations
                .OrderBy(registration => registration.Order)
                .ThenBy(registration => registration.RegistrationIndex)
                .ToArray());
    }

    private static void ValidateSources(InlineEventSource sources)
    {
        if (sources == 0 || (sources & ~InlineEventSource.All) != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(InlineEventHandlerRegistrationOptions.Sources)} must select Stream, EntityOutbox, or All.");
        }
    }
}

internal sealed record InlineEventHandlerConfiguration<TDbContext>(
    int MaxDispatchCount,
    IReadOnlyList<InlineEventHandlerRegistration> Registrations)
    where TDbContext : DbContext;

internal sealed class InlineEventHandlerRegistrationMarker<TDbContext>
    where TDbContext : DbContext;

internal abstract class InlineEventHandlerRegistration(
    Type handlerType,
    Type eventType,
    int order,
    InlineEventSource sources,
    int registrationIndex)
{
    internal Type HandlerType { get; } = handlerType;

    internal Type EventType { get; } = eventType;

    internal int Order { get; } = order;

    internal InlineEventSource Sources { get; } = sources;

    internal int RegistrationIndex { get; } = registrationIndex;

    internal abstract Task Handle(
        IServiceProvider serviceProvider,
        IEventEnvelope envelope,
        CancellationToken ct);
}

internal sealed class InlineEventHandlerRegistration<THandler, TEvent>(
    int order,
    InlineEventSource sources,
    int registrationIndex) : InlineEventHandlerRegistration(
        typeof(THandler),
        typeof(TEvent),
        order,
        sources,
        registrationIndex)
    where THandler : class, IInlineEventHandler<TEvent>
    where TEvent : class
{
    internal override Task Handle(
        IServiceProvider serviceProvider,
        IEventEnvelope envelope,
        CancellationToken ct)
    {
        var typedEnvelope = envelope switch
        {
            IEvent streamEvent => (IEventEnvelope<TEvent>)new TypedEvent<TEvent>(streamEvent),
            IOutboxEvent outboxEvent => new TypedOutboxEvent<TEvent>(outboxEvent),
            _ => throw new InvalidOperationException(
                $"Unsupported inline event envelope '{envelope.GetType().FullName}'.")
        };

        return serviceProvider.GetRequiredService<THandler>().Handle(typedEnvelope, ct);
    }
}

internal sealed class TypedOutboxEvent<TEvent>(IOutboxEvent source) : IOutboxEvent<TEvent>
    where TEvent : class
{
    public Guid Id => source.Id;

    public long Sequence => source.Sequence;

    public TEvent Data => (TEvent)source.Data;

    object IOutboxEvent.Data => Data;

    object IEventEnvelope.Data => Data;

    public Type EventType => source.EventType;

    public DateTimeOffset Timestamp => source.Timestamp;

    public Guid TenantId => source.TenantId;

    public string SourceEntityType => source.SourceEntityType;

    public string SourceEntityKey => source.SourceEntityKey;

    public EntityChangeKind ChangeKind => source.ChangeKind;
}
