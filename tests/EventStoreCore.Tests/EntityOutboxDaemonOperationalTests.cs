using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public sealed class EntityOutboxDaemonOperationalTests
{
    [Fact]
    public async Task Typed_filters_advance_checkpoint_and_handlers_are_scoped_without_an_ambient_transaction()
    {
        var tenantId = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync(services =>
            services.AddOutboxSubscription<TypedSubscription, OrderAdded>(options =>
            {
                options.Name = "typed-orders";
                options.IncludeLogicalEventType("order_added");
                options.IncludeTenant(tenantId);
                options.IncludeSourceEntity<Order>();
                options.IncludeChangeKind(EntityChangeKind.Added);
            }));
        await fixture.AddMessageAsync(
            tenantId,
            "ignored_event",
            typeof(OrderAdded).AssemblyQualifiedName!,
            typeof(Order).AssemblyQualifiedName!);
        var handledSequence = await fixture.AddMessageAsync(
            tenantId,
            "order_added",
            typeof(OrderAdded).AssemblyQualifiedName!,
            typeof(Order).AssemblyQualifiedName!);

        using var handlerScope = fixture.Provider.CreateScope();
        using var checkpointScope = fixture.Provider.CreateScope();
        var registration = handlerScope.ServiceProvider
            .GetServices<OutboxSubscriptionRegistration>()
            .Single();
        var subscription = registration.Resolve(handlerScope.ServiceProvider);
        var daemon = fixture.Provider.GetRequiredService<EntityOutboxDaemon<OutboxDbContext>>();

        var processed = await daemon.ProcessNextBatchAsync(
            checkpointScope,
            subscription,
            registration,
            CheckpointScopeKey.Global,
            TestContext.Current.CancellationToken);

        var handler = handlerScope.ServiceProvider.GetRequiredService<TypedSubscription>();
        Assert.Equal(2, processed);
        Assert.Single(handler.Events);
        Assert.False(handler.SawAmbientTransaction);
        var checkpoint = await checkpointScope.ServiceProvider.GetRequiredService<OutboxDbContext>()
            .Set<DbOutboxSubscription>()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(handledSequence, checkpoint.Sequence);
    }

    [Fact]
    public async Task Skip_unknown_policy_advances_without_invoking_subscription()
    {
        await using var fixture = await Fixture.CreateAsync(services =>
            services.AddOutboxSubscription<RecordingSubscription>(options =>
            {
                options.Name = "skip-unknown";
                options.UnknownEventPolicy = UnknownEventPolicy.Skip;
            }));
        var sequence = await fixture.AddMessageAsync(
            Guid.Empty,
            "removed_event",
            "Missing.Event, Missing.Assembly",
            typeof(Order).AssemblyQualifiedName!);

        var processed = await fixture.ProcessAsync();

        Assert.Equal(1, processed);
        using var scope = fixture.Provider.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<RecordingSubscription>().Events);
        var checkpoint = await scope.ServiceProvider.GetRequiredService<OutboxDbContext>()
            .Set<DbOutboxSubscription>()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(sequence, checkpoint.Sequence);
        Assert.Equal(SubscriptionState.Active, checkpoint.State);
    }

    [Fact]
    public async Task Custom_unknown_policy_receives_raw_event_and_advances()
    {
        UnknownOutboxEventContext? observed = null;
        await using var fixture = await Fixture.CreateAsync(services =>
            services.AddOutboxSubscription<RecordingSubscription>(options =>
            {
                options.Name = "custom-unknown";
                options.HandleUnknown((context, _) =>
                {
                    observed = context;
                    return ValueTask.CompletedTask;
                });
            }));
        var sequence = await fixture.AddMessageAsync(
            Guid.Empty,
            "removed_event",
            "Missing.Event, Missing.Assembly",
            typeof(Order).AssemblyQualifiedName!);

        var processed = await fixture.ProcessAsync();

        Assert.Equal(1, processed);
        Assert.NotNull(observed);
        Assert.Equal(sequence, observed.Sequence);
        Assert.Equal("removed_event", observed.LogicalTypeName);
        Assert.IsType<InvalidOperationException>(observed.Exception);
    }

    [Fact]
    public async Task Quarantine_unknown_policy_dead_letters_and_notifies_health_and_observers()
    {
        var observer = new RecordingFaultObserver();
        await using var fixture = await Fixture.CreateAsync(
            services =>
            {
                services.AddSingleton<IDaemonFaultObserver>(observer);
                services.AddOutboxSubscription<RecordingSubscription>(options =>
                {
                    options.Name = "quarantine-unknown";
                    options.UnknownEventPolicy = UnknownEventPolicy.Quarantine;
                });
            });
        var sequence = await fixture.AddMessageAsync(
            Guid.Empty,
            "removed_event",
            "Missing.Event, Missing.Assembly",
            typeof(Order).AssemblyQualifiedName!);

        var processed = await fixture.ProcessAsync();

        Assert.Equal(0, processed);
        using var scope = fixture.Provider.CreateScope();
        var checkpoint = await scope.ServiceProvider.GetRequiredService<OutboxDbContext>()
            .Set<DbOutboxSubscription>()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SubscriptionState.DeadLettered, checkpoint.State);
        Assert.Equal(sequence, checkpoint.FailedEventSequence);
        var notification = Assert.Single(observer.Notifications);
        Assert.Equal("quarantine-unknown", notification.Identity);
        Assert.Equal("outbox-subscription", notification.DaemonKind);
        Assert.Equal("DeadLettered", notification.State);
        Assert.Equal(
            DaemonHealthStatus.Unhealthy,
            fixture.Provider.GetRequiredService<DaemonHealthMonitor>()
                .CheckHealth(TimeSpan.FromMinutes(1))
                .Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            Provider = provider;
        }

        internal ServiceProvider Provider { get; }

        internal static async Task<Fixture> CreateAsync(
            Action<IServiceCollection> configure)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<RecordingSink>();
            services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection));
            services.AddEntityOutbox<OutboxDbContext>(_ => { });
            configure(services);
            services.AddEntityOutboxDaemon<OutboxDbContext>(
                _ => new FakeLockProvider(),
                options => options.PollingInterval = TimeSpan.FromHours(1));
            var provider = services.BuildServiceProvider();

            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OutboxDbContext>()
                .Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            return new Fixture(connection, provider);
        }

        internal async Task<long> AddMessageAsync(
            Guid tenantId,
            string logicalType,
            string clrType,
            string sourceEntityType)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var message = new DbOutboxMessage
            {
                EventId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = clrType,
                TypeName = logicalType,
                Data = $$"""{"id":"{{Guid.NewGuid():D}}"}""",
                Timestamp = DateTimeOffset.UtcNow,
                SourceEntityType = sourceEntityType,
                SourceEntityKey = """{"id":"1"}""",
                ChangeKind = EntityChangeKind.Added
            };
            db.Add(message);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return message.Sequence;
        }

        internal async Task<int> ProcessAsync()
        {
            using var handlerScope = Provider.CreateScope();
            using var checkpointScope = Provider.CreateScope();
            var registration = handlerScope.ServiceProvider
                .GetServices<OutboxSubscriptionRegistration>()
                .Single();
            return await Provider.GetRequiredService<EntityOutboxDaemon<OutboxDbContext>>()
                .ProcessNextBatchAsync(
                    checkpointScope,
                    registration.Resolve(handlerScope.ServiceProvider),
                    registration,
                    CheckpointScopeKey.Global,
                    TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);
        }
    }

    private sealed class TypedSubscription(OutboxDbContext dbContext)
        : IOutboxSubscription<OrderAdded>
    {
        internal List<IOutboxEvent<OrderAdded>> Events { get; } = [];

        internal bool SawAmbientTransaction { get; private set; }

        public Task Handle(IOutboxEvent<OrderAdded> @event, CancellationToken ct)
        {
            SawAmbientTransaction |= dbContext.Database.CurrentTransaction is not null;
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSubscription(RecordingSink sink) : IOutboxSubscription
    {
        internal IReadOnlyList<IOutboxEvent> Events => sink.Events;

        public Task Handle(IOutboxEvent @event, CancellationToken ct)
        {
            sink.Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSink
    {
        internal List<IOutboxEvent> Events { get; } = [];
    }

    private sealed record OrderAdded(Guid Id);

    private sealed class Order;

    private sealed class RecordingFaultObserver : IDaemonFaultObserver
    {
        internal List<DaemonFaultNotification> Notifications { get; } = [];

        public ValueTask OnFaultAsync(
            DaemonFaultNotification notification,
            CancellationToken ct)
        {
            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeLockProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new FakeLock(name);
    }

    private sealed class FakeLock(string name) : IDistributedLock
    {
        public string Name { get; } = name;

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new FakeLockHandle();

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new FakeLockHandle());

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            new FakeLockHandle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new FakeLockHandle());
    }

    private sealed class FakeLockHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
