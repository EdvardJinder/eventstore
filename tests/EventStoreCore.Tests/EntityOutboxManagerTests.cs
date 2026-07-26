using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public sealed class EntityOutboxManagerTests
{
    [Fact]
    public void Stable_names_must_be_unique()
    {
        var services = new ServiceCollection();
        services.AddOutboxSubscription<RecordingSubscription>(
            options => options.Name = "orders");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddOutboxSubscription<OtherSubscription>(
                options => options.Name = "orders"));

        Assert.Contains("orders", exception.Message);
    }

    [Fact]
    public async Task Daemon_and_manager_use_the_configured_stable_name()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMessageAsync(Guid.Empty, DateTimeOffset.UtcNow);

        var daemon = fixture.Provider.GetRequiredService<EntityOutboxDaemon<OutboxDbContext>>();
        var subscription = fixture.Provider.GetRequiredService<RecordingSubscription>();
        using (var processScope = fixture.Provider.CreateScope())
        {
            var processed = await daemon.ProcessNextBatchAsync(
                processScope,
                subscription,
                CheckpointScopeKey.Global,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, processed);
        }

        await using var scope = fixture.Provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOutboxSubscriptionManager>();
        var status = await manager.GetStatusAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(Fixture.SubscriptionName, status.SubscriptionName);
        Assert.Equal(1, status.Position);
        Assert.Equal(100, status.ProgressPercentage);
    }

    [Fact]
    public async Task Manager_recovers_and_replays_failed_subscriptions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstTimestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        var firstSequence = await fixture.AddMessageAsync(Guid.Empty, firstTimestamp);
        var secondSequence = await fixture.AddMessageAsync(
            Guid.Empty,
            firstTimestamp.AddMinutes(1));

        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var checkpoint = new DbOutboxSubscription
        {
            SubscriptionAssemblyQualifiedName = Fixture.SubscriptionName,
            Sequence = firstSequence,
            State = SubscriptionState.DeadLettered,
            AttemptCount = 3,
            LastError = "publisher unavailable",
            FailedEventSequence = secondSequence,
            LastAttemptAt = DateTimeOffset.UtcNow
        };
        db.Add(checkpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IOutboxSubscriptionManager>();
        var failedEvent = await manager.GetFailedEventAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);
        Assert.NotNull(failedEvent);
        Assert.Equal(secondSequence, failedEvent.Sequence);
        Assert.Equal("order_added", failedEvent.EventType);
        Assert.Equal("publisher unavailable", failedEvent.SubscriptionError);

        await manager.RetryFailedEventAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);
        Assert.Equal(SubscriptionState.Active, checkpoint.State);
        Assert.Null(checkpoint.FailedEventSequence);

        await manager.PauseAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);
        Assert.Equal(SubscriptionState.Paused, checkpoint.State);

        await manager.ResumeAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);
        Assert.Equal(SubscriptionState.Active, checkpoint.State);

        checkpoint.State = SubscriptionState.Faulted;
        checkpoint.LastError = "bad payload";
        checkpoint.FailedEventSequence = secondSequence;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await manager.SkipFailedEventAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);
        Assert.Equal(secondSequence, checkpoint.Sequence);
        Assert.Equal(SubscriptionState.Active, checkpoint.State);

        await manager.ReplayAsync(
            Fixture.SubscriptionName,
            startSequence: secondSequence,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(firstSequence, checkpoint.Sequence);

        var status = await manager.GetStatusAsync(
            Fixture.SubscriptionName,
            TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(50, status.ProgressPercentage);
    }

    [Fact]
    public async Task Manager_isolates_tenant_scoped_status_and_replay()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantASequence = await fixture.AddMessageAsync(tenantA, DateTimeOffset.UtcNow);
        await fixture.AddMessageAsync(tenantB, DateTimeOffset.UtcNow);

        await using var scope = fixture.Provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOutboxSubscriptionManager>();
        await manager.ReplayAsync(
            Fixture.SubscriptionName,
            tenantA,
            startSequence: tenantASequence,
            ct: TestContext.Current.CancellationToken);

        var status = await manager.GetStatusAsync(
            Fixture.SubscriptionName,
            tenantA,
            TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(CheckpointScope.Tenant, status.CheckpointScope);
        Assert.Equal(tenantA, status.TenantId);
        Assert.Equal(0, status.Position);
        Assert.Equal(1, status.TotalEvents);
    }

    [Fact]
    public async Task Cleanup_waits_for_every_registered_subscription_to_initialize()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMessageAsync(Guid.Empty, DateTimeOffset.UtcNow);

        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        db.Add(new DbOutboxSubscription
        {
            SubscriptionAssemblyQualifiedName = "another-subscription",
            Sequence = 1
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var deleted = await scope.ServiceProvider.GetRequiredService<IOutboxReader>()
            .CleanupAsync(long.MaxValue, TestContext.Current.CancellationToken);

        Assert.Equal(0, deleted);
        Assert.Single(await db.Set<DbOutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_waits_for_every_registered_tenant_checkpoint_to_initialize()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantASequence = await fixture.AddMessageAsync(tenantA, DateTimeOffset.UtcNow);
        var tenantBSequence = await fixture.AddMessageAsync(tenantB, DateTimeOffset.UtcNow);

        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        db.Add(new DbOutboxSubscription
        {
            SubscriptionAssemblyQualifiedName = Fixture.SubscriptionName,
            CheckpointScope = CheckpointScope.Tenant,
            TenantId = tenantA,
            Sequence = tenantASequence
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reader = scope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var deleted = await reader.CleanupAsync(
            long.MaxValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, deleted);

        db.Add(new DbOutboxSubscription
        {
            SubscriptionAssemblyQualifiedName = Fixture.SubscriptionName,
            CheckpointScope = CheckpointScope.Tenant,
            TenantId = tenantB,
            Sequence = tenantBSequence
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        deleted = await reader.CleanupAsync(
            long.MaxValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, deleted);
        Assert.Single(await db.Set<DbOutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string SubscriptionName = "publish-orders";
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            Provider = provider;
        }

        internal ServiceProvider Provider { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection));
            services.AddEntityOutbox<OutboxDbContext>(_ => { });
            services.AddOutboxSubscription<RecordingSubscription>(
                options => options.Name = SubscriptionName);
            services.AddEntityOutboxDaemon<OutboxDbContext>(
                _ => new FakeLockProvider(),
                options => options.PollingInterval = TimeSpan.FromHours(1));
            var provider = services.BuildServiceProvider();

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            return new Fixture(connection, provider);
        }

        internal async Task<long> AddMessageAsync(Guid tenantId, DateTimeOffset timestamp)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var message = new DbOutboxMessage
            {
                EventId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = typeof(OrderAdded).AssemblyQualifiedName!,
                TypeName = "order_added",
                Data = """{"id":"00000000-0000-0000-0000-000000000001"}""",
                Timestamp = timestamp,
                SourceEntityType = "Example.Order, Example",
                SourceEntityKey = """{"id":"00000000-0000-0000-0000-000000000001"}""",
                ChangeKind = EntityChangeKind.Added
            };
            db.Add(message);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return message.Sequence;
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

    private sealed record OrderAdded(Guid Id);

    private sealed class RecordingSubscription : IOutboxSubscription
    {
        public Task Handle(IOutboxEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class OtherSubscription : IOutboxSubscription
    {
        public Task Handle(IOutboxEvent @event, CancellationToken ct) => Task.CompletedTask;
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
