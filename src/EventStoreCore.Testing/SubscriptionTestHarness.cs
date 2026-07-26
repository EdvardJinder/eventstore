using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Testing;

/// <summary>
/// Creates deterministic, in-memory harnesses for testing EventStoreCore subscriptions.
/// </summary>
public static class SubscriptionTestHarness
{
    /// <summary>
    /// Creates a harness for an untyped subscription.
    /// </summary>
    /// <typeparam name="TSubscription">The subscription implementation type.</typeparam>
    /// <param name="subscription">The subscription instance to exercise.</param>
    /// <param name="configureRegistration">
    /// Optional stable-name, filter, and unknown-event configuration.
    /// </param>
    /// <param name="configureDaemon">Optional batch, checkpoint, and retry configuration.</param>
    /// <param name="timeProvider">Optional clock used for retry timestamps.</param>
    /// <returns>A harness that owns the in-memory store used by the subscription test.</returns>
    public static SubscriptionTestHarness<TSubscription> For<TSubscription>(
        TSubscription subscription,
        Action<SubscriptionRegistrationOptions>? configureRegistration = null,
        Action<SubscriptionOptions>? configureDaemon = null,
        TimeProvider? timeProvider = null)
        where TSubscription : class, ISubscription
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var registrationOptions = new SubscriptionRegistrationOptions();
        configureRegistration?.Invoke(registrationOptions);

        return SubscriptionTestHarness<TSubscription>.Create(
            subscription,
            subscription,
            registrationOptions,
            configureDaemon,
            timeProvider);
    }

    /// <summary>
    /// Creates a harness for a strongly typed subscription.
    /// </summary>
    /// <typeparam name="TSubscription">The subscription implementation type.</typeparam>
    /// <typeparam name="TEvent">The event payload handled by the subscription.</typeparam>
    /// <param name="subscription">The subscription instance to exercise.</param>
    /// <param name="configureRegistration">
    /// Optional stable-name, filter, and unknown-event configuration.
    /// </param>
    /// <param name="configureDaemon">Optional batch, checkpoint, and retry configuration.</param>
    /// <param name="timeProvider">Optional clock used for retry timestamps.</param>
    /// <returns>A harness that owns the in-memory store used by the subscription test.</returns>
    public static SubscriptionTestHarness<TSubscription> For<TSubscription, TEvent>(
        TSubscription subscription,
        Action<SubscriptionRegistrationOptions>? configureRegistration = null,
        Action<SubscriptionOptions>? configureDaemon = null,
        TimeProvider? timeProvider = null)
        where TSubscription : class, ISubscription<TEvent>
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var registrationOptions = new SubscriptionRegistrationOptions();
        configureRegistration?.Invoke(registrationOptions);
        registrationOptions.IncludeEventType(typeof(TEvent));

        return SubscriptionTestHarness<TSubscription>.Create(
            subscription,
            new TypedSubscriptionAdapter<TSubscription, TEvent>(subscription),
            registrationOptions,
            configureDaemon,
            timeProvider);
    }
}

/// <summary>
/// Provides deterministic, stateful execution of a subscription against an isolated in-memory event log.
/// </summary>
/// <typeparam name="TSubscription">The application subscription type.</typeparam>
public sealed class SubscriptionTestHarness<TSubscription> : IAsyncDisposable
    where TSubscription : class
{
    private const int DefaultMaximumBatches = 100;

    private readonly SubscriptionHarnessDbContext _dbContext;
    private readonly ServiceProvider _serviceProvider;
    private readonly SubscriptionDaemon<SubscriptionHarnessDbContext> _daemon;
    private readonly ISubscriptionManager _manager;
    private readonly SubscriptionRegistration _registration;
    private readonly IEventStoreSerializer _serializer;
    private long _lastSequence;

    private SubscriptionTestHarness(
        TSubscription subscription,
        SubscriptionHarnessDbContext dbContext,
        ServiceProvider serviceProvider,
        SubscriptionDaemon<SubscriptionHarnessDbContext> daemon,
        ISubscriptionManager manager,
        SubscriptionRegistration registration,
        IEventStoreSerializer serializer,
        TimeProvider timeProvider)
    {
        Subscription = subscription;
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
        _daemon = daemon;
        _manager = manager;
        _registration = registration;
        _serializer = serializer;
        TimeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the application subscription instance exercised by this harness.
    /// </summary>
    public TSubscription Subscription { get; }

    /// <summary>
    /// Gets the stable subscription name used for checkpoints and management operations.
    /// </summary>
    public string Name => _registration.Name;

    /// <summary>
    /// Gets the clock used for retry timestamps and daemon decisions.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Seeds materialized events into the isolated event log without invoking the subscription.
    /// Events whose global sequence is zero receive the next deterministic sequence.
    /// </summary>
    /// <param name="events">The events to seed, in global processing order.</param>
    public void Given(params IEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var @event in events)
        {
            ArgumentNullException.ThrowIfNull(@event);
            var eventType = @event.EventType;
            if (eventType.IsValueType)
            {
                throw new ArgumentException(
                    $"Event payload type '{eventType.FullName}' is a value type. Event payloads must be reference types.",
                    nameof(events));
            }

            var metadata = @event.Metadata;
            _dbContext.Set<DbEvent>().Add(new DbEvent
            {
                EventId = @event.Id == Guid.Empty ? Guid.NewGuid() : @event.Id,
                StreamId = @event.StreamId == Guid.Empty ? Guid.NewGuid() : @event.StreamId,
                StreamType = @event.StreamType ?? string.Empty,
                TenantId = @event.TenantId,
                Sequence = ResolveSequence(@event.Sequence),
                Version = @event.Version <= 0 ? 1 : @event.Version,
                Type = eventType.AssemblyQualifiedName
                    ?? throw new ArgumentException(
                        $"Event type '{eventType.FullName}' does not have an assembly-qualified name.",
                        nameof(events)),
                TypeName = string.IsNullOrWhiteSpace(@event.TypeName)
                    ? EventTypeNameHelper.ToSnakeCase(eventType)
                    : @event.TypeName,
                Data = _serializer.Serialize(@event.Data, eventType),
                Timestamp = @event.Timestamp == default ? TimeProvider.GetUtcNow() : @event.Timestamp,
                CorrelationId = metadata.CorrelationId,
                CausationId = metadata.CausationId,
                Actor = metadata.Actor,
                Headers = EventHeaders.Serialize(metadata.Headers),
                SchemaVersion = metadata.SchemaVersion
            });
        }

        _dbContext.SaveChanges();
    }

    /// <summary>
    /// Seeds an event whose CLR payload type cannot be resolved, allowing unknown-event policies to be tested.
    /// </summary>
    /// <param name="logicalTypeName">The persisted logical event type name.</param>
    /// <param name="clrTypeName">The unresolved persisted CLR type name.</param>
    /// <param name="json">The raw JSON payload.</param>
    /// <param name="eventId">Optional stable event identifier.</param>
    /// <param name="streamId">Optional stream identifier.</param>
    /// <param name="streamType">The logical stream type.</param>
    /// <param name="tenantId">Optional tenant identifier. The empty identifier is used when omitted.</param>
    /// <param name="sequence">Optional positive global sequence. The next sequence is used when omitted.</param>
    /// <param name="version">The positive version within the stream.</param>
    /// <param name="timestamp">Optional persisted timestamp. The harness clock is used when omitted.</param>
    public void GivenUnknown(
        string logicalTypeName,
        string clrTypeName,
        string json,
        Guid? eventId = null,
        Guid? streamId = null,
        string streamType = "",
        Guid? tenantId = null,
        long? sequence = null,
        long version = 1,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clrTypeName);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(streamType);
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "An explicitly supplied sequence must be greater than zero.");
        }
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "The stream version must be greater than zero.");
        }

        _dbContext.Set<DbEvent>().Add(new DbEvent
        {
            EventId = eventId ?? Guid.NewGuid(),
            StreamId = streamId ?? Guid.NewGuid(),
            StreamType = streamType,
            TenantId = tenantId ?? Guid.Empty,
            Sequence = ResolveSequence(sequence ?? 0),
            Version = version,
            Type = clrTypeName,
            TypeName = logicalTypeName,
            Data = json,
            Timestamp = timestamp ?? TimeProvider.GetUtcNow(),
            Headers = "{}",
            SchemaVersion = 1
        });
        _dbContext.SaveChanges();
    }

    /// <summary>
    /// Processes one configured daemon batch.
    /// </summary>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of event-log entries advanced during the batch.</returns>
    public async Task<int> ProcessNextBatchAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        return await _daemon.ProcessNextBatchAsync(
            scope,
            _registration,
            cancellationToken,
            tenantId.HasValue
                ? CheckpointScopeKey.Tenant(tenantId.Value)
                : CheckpointScopeKey.Global);
    }

    /// <summary>
    /// Processes configured daemon batches until no further progress can be made.
    /// </summary>
    /// <param name="maximumBatches">Maximum batches to process before failing the test.</param>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of event-log entries advanced.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the maximum batch count is reached before the daemon becomes idle.
    /// </exception>
    public async Task<int> ProcessUntilIdleAsync(
        int maximumBatches = DefaultMaximumBatches,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (maximumBatches <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBatches),
                "The maximum batch count must be greater than zero.");
        }

        var total = 0;
        for (var batch = 0; batch < maximumBatches; batch++)
        {
            var processed = await ProcessNextBatchAsync(tenantId, cancellationToken);
            total += processed;
            if (processed == 0)
            {
                return total;
            }
        }

        throw new InvalidOperationException(
            $"Subscription '{Name}' did not become idle within {maximumBatches} batches.");
    }

    /// <summary>
    /// Gets the current public subscription status.
    /// </summary>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current status.</returns>
    public async Task<SubscriptionStatusDto> GetStatusAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var status = tenantId.HasValue
            ? await _manager.GetStatusAsync(Name, tenantId.Value, cancellationToken)
            : await _manager.GetStatusAsync(Name, cancellationToken);
        return status
            ?? throw new InvalidOperationException($"Subscription '{Name}' is not registered.");
    }

    /// <summary>
    /// Gets the current failed event through the public subscription management contract.
    /// </summary>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failed event, or <see langword="null"/> when the subscription is not faulted.</returns>
    public Task<SubscriptionFailedEventDto?> GetFailedEventAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return tenantId.HasValue
            ? _manager.GetFailedEventAsync(Name, tenantId.Value, cancellationToken)
            : _manager.GetFailedEventAsync(Name, cancellationToken);
    }

    /// <summary>
    /// Clears the current failure so the same event can be attempted again.
    /// </summary>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RetryFailedEventAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return tenantId.HasValue
            ? _manager.RetryFailedEventAsync(Name, tenantId.Value, cancellationToken)
            : _manager.RetryFailedEventAsync(Name, cancellationToken);
    }

    /// <summary>
    /// Skips the current failed event and resumes after it.
    /// </summary>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SkipFailedEventAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return tenantId.HasValue
            ? _manager.SkipFailedEventAsync(Name, tenantId.Value, cancellationToken)
            : _manager.SkipFailedEventAsync(Name, cancellationToken);
    }

    /// <summary>
    /// Resets the checkpoint so events can be replayed.
    /// </summary>
    /// <param name="startSequence">Optional inclusive global sequence from which to replay.</param>
    /// <param name="fromTimestamp">Optional timestamp from which to replay.</param>
    /// <param name="tenantId">
    /// Optional tenant checkpoint scope. When omitted, the global checkpoint is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ReplayAsync(
        long? startSequence = null,
        DateTimeOffset? fromTimestamp = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return tenantId.HasValue
            ? _manager.ReplayAsync(
                Name,
                tenantId.Value,
                startSequence,
                fromTimestamp,
                cancellationToken)
            : _manager.ReplayAsync(Name, startSequence, fromTimestamp, cancellationToken);
    }

    /// <summary>
    /// Disposes the isolated service provider and in-memory store.
    /// </summary>
    public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

    internal static SubscriptionTestHarness<TSubscription> Create(
        TSubscription subscription,
        ISubscription daemonSubscription,
        SubscriptionRegistrationOptions registrationOptions,
        Action<SubscriptionOptions>? configureDaemon,
        TimeProvider? timeProvider)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var daemonOptions = new SubscriptionOptions();
        configureDaemon?.Invoke(daemonOptions);

        var dbContext = new SubscriptionHarnessDbContext(Guid.NewGuid().ToString("N"));
        dbContext.Database.EnsureCreated();

        var serializer = new SystemTextJsonEventStoreSerializer();
        var registration = new SubscriptionRegistration
        {
            Name = string.IsNullOrWhiteSpace(registrationOptions.Name)
                ? typeof(TSubscription).AssemblyQualifiedName!
                : registrationOptions.Name,
            Subscription = daemonSubscription,
            Options = registrationOptions
        };
        var lockProvider = new ImmediateLockProvider();
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(subscription);
        services.AddSingleton<ISubscription>(daemonSubscription);
        services.AddSingleton(registration);
        services.AddSingleton<IEventStoreSerializer>(serializer);
        var serviceProvider = services.BuildServiceProvider();

        var daemon = new SubscriptionDaemon<SubscriptionHarnessDbContext>(
            NullLogger<SubscriptionDaemon<SubscriptionHarnessDbContext>>.Instance,
            serviceProvider,
            lockProvider,
            Options.Create(daemonOptions),
            clock);
        var manager = new SubscriptionManager<SubscriptionHarnessDbContext>(
            dbContext,
            lockProvider,
            [daemonSubscription],
            NullLogger<SubscriptionManager<SubscriptionHarnessDbContext>>.Instance,
            [registration]);

        return new SubscriptionTestHarness<TSubscription>(
            subscription,
            dbContext,
            serviceProvider,
            daemon,
            manager,
            registration,
            serializer,
            clock);
    }

    private long ResolveSequence(long sequence)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "The global sequence cannot be negative.");
        }

        if (sequence == 0)
        {
            return ++_lastSequence;
        }

        if (sequence <= _lastSequence)
        {
            throw new InvalidOperationException(
                $"Global sequence {sequence} is not greater than the previously seeded sequence {_lastSequence}.");
        }

        _lastSequence = sequence;
        return sequence;
    }

    private sealed class SubscriptionHarnessDbContext(string databaseName) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);
        }
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new ImmediateLock(name);
    }

    private sealed class ImmediateLock(string name) : IDistributedLock
    {
        public string Name { get; } = name;

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new ImmediateLockHandle();

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new(new ImmediateLockHandle());

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            new ImmediateLockHandle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            new(new ImmediateLockHandle());
    }

    private sealed class ImmediateLockHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
