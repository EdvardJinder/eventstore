using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Tests;

public class SubscriptionDaemonExecutionTests
{
    private class ExecutionDbContext : DbContext
    {
        public ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
        }
    }

    private sealed class CountingExecutionDbContext : ExecutionDbContext
    {
        public CountingExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : base(options)
        {
        }

        public int SubscriptionSaveChangesCount { get; private set; }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            CountSubscriptionCheckpointWrite();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        public void ResetCounters()
        {
            SubscriptionSaveChangesCount = 0;
        }

        private void CountSubscriptionCheckpointWrite()
        {
            if (ChangeTracker.Entries<DbSubscription>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                SubscriptionSaveChangesCount++;
            }
        }
    }

    private sealed class RecordingSubscription : ISubscription
    {
        public bool Handled { get; private set; }
        public int HandledCount { get; private set; }
        public Task Handle(IEvent @event, CancellationToken ct)
        {
            Handled = true;
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscription : ISubscription
    {
        private readonly int _throwOnAttempt;

        public ThrowingSubscription(int throwOnAttempt = 1)
        {
            _throwOnAttempt = throwOnAttempt;
        }

        public int HandledCount { get; private set; }

        public Task Handle(IEvent @event, CancellationToken ct)
        {
            HandledCount++;

            if (HandledCount == _throwOnAttempt)
            {
                throw new InvalidOperationException("Subscription handler failed.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class MutatingThrowingScopedSubscription : IScopedSubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task HandleAsync(
            DbContext dbContext,
            IServiceProvider services,
            IEvent @event,
            CancellationToken ct)
        {
            dbContext.Set<DbProjectionStatus>().Add(new DbProjectionStatus
            {
                ProjectionName = "must-not-commit",
                Position = @event.Version
            });
            throw new InvalidOperationException("Scoped handler failed after mutation.");
        }
    }

    private sealed class TenantPoisonSubscription : ISubscription
    {
        private readonly Guid _poisonTenantId;

        public TenantPoisonSubscription(Guid poisonTenantId)
        {
            _poisonTenantId = poisonTenantId;
        }

        public List<Guid> HandledTenantIds { get; } = [];

        public Task Handle(IEvent @event, CancellationToken ct)
        {
            if (@event.TenantId == _poisonTenantId)
            {
                throw new InvalidOperationException("Tenant event failed.");
            }

            HandledTenantIds.Add(@event.TenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLockProvider : IDistributedLockProvider
    {
        public bool ReturnNullHandle { get; set; }
        public TimeSpan? LastAcquireTimeout { get; set; }

        public IDistributedLock CreateLock(string name) => new FakeLock(this, name);
    }

    private sealed class FakeLock : IDistributedLock
    {
        private readonly FakeLockProvider _provider;
        public FakeLock(FakeLockProvider provider, string name)
        {
            _provider = provider;
            Name = name;
        }

        public string Name { get; }

        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new FakeHandle();

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            _provider.LastAcquireTimeout = timeout;
            return new ValueTask<IDistributedSynchronizationHandle>(new FakeHandle());
        }

        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            return _provider.ReturnNullHandle ? null : new FakeHandle();
        }

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            return new ValueTask<IDistributedSynchronizationHandle?>(_provider.ReturnNullHandle ? null : new FakeHandle());
        }
    }

    private sealed class FakeHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task RunExecuteAsync(SubscriptionDaemon<ExecutionDbContext> daemon, CancellationToken token)
    {
        var method = typeof(SubscriptionDaemon<ExecutionDbContext>).GetMethod(
            "ExecuteAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { token })!;
        await task;
    }

    private static ServiceProvider BuildProvider<TDbContext>(TDbContext db, ISubscription subscription)
        where TDbContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ISubscription>(subscription);
        return services.BuildServiceProvider();
    }

    private static ExecutionDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ExecutionDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesEventAndStopsOnCancellation()
    {
        var db = BuildDbContext();
        var subscription = new RecordingSubscription();
        var provider = BuildProvider(db, subscription);
        var lockProvider = new FakeLockProvider();
        var options = Options.Create(new SubscriptionOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(5),
            LockTimeout = TimeSpan.FromMilliseconds(5),
            RetryDelay = TimeSpan.FromMilliseconds(5)
        });

        db.Streams.StartStream(Guid.NewGuid(), events: [new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var storedEvent = await db.Set<DbEvent>().FirstAsync(TestContext.Current.CancellationToken);
        if (storedEvent.Sequence == 0)
        {
            storedEvent.Sequence = 1;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await RunExecuteAsync(daemon, cts.Token);

        Assert.True(subscription.Handled);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsWhenLockUnavailable()
    {
        var db = BuildDbContext();
        var subscription = new RecordingSubscription();
        var provider = BuildProvider(db, subscription);
        var lockProvider = new FakeLockProvider { ReturnNullHandle = true };
        var options = Options.Create(new SubscriptionOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(5),
            LockTimeout = TimeSpan.FromMilliseconds(5),
            RetryDelay = TimeSpan.FromMilliseconds(5)
        });

        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await RunExecuteAsync(daemon, cts.Token);

        Assert.False(subscription.Handled);
    }

    [Fact]
    public async Task ProcessNextBatchAsync_ProcessesConfiguredBatchSize()
    {
        var db = BuildDbContext();
        var subscription = new RecordingSubscription();
        var provider = BuildProvider(db, subscription);
        var lockProvider = new FakeLockProvider();
        var options = Options.Create(new SubscriptionOptions
        {
            BatchSize = 2,
            CheckpointFrequency = 2
        });

        db.Streams.StartStream(Guid.NewGuid(), events: [new object(), new object(), new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequencesAsync(db);

        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            options);

        var processed = await daemon.ProcessNextBatchAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        var subscriptionEntity = await FindSubscriptionAsync(db, subscription.GetType().AssemblyQualifiedName!, CheckpointScope.Global, Guid.Empty);

        Assert.Equal(2, processed);
        Assert.Equal(2, subscription.HandledCount);
        Assert.NotNull(subscriptionEntity);
        Assert.Equal(2, subscriptionEntity.Sequence);
    }

    [Fact]
    public async Task ProcessNextBatchAsync_CheckpointsAtConfiguredFrequency()
    {
        var db = BuildCountingDbContext();
        var subscription = new RecordingSubscription();
        var provider = BuildProvider(db, subscription);
        var lockProvider = new FakeLockProvider();
        var options = Options.Create(new SubscriptionOptions
        {
            BatchSize = 3,
            CheckpointFrequency = 2
        });

        db.Streams.StartStream(Guid.NewGuid(), events: [new object(), new object(), new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequencesAsync(db);
        db.ResetCounters();

        var daemon = new SubscriptionDaemon<CountingExecutionDbContext>(
            NullLogger<SubscriptionDaemon<CountingExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            options);

        var processed = await daemon.ProcessNextBatchAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        Assert.Equal(3, processed);
        Assert.Equal(3, subscription.HandledCount);
        Assert.Equal(2, db.SubscriptionSaveChangesCount);
    }

    [Fact]
    public async Task ProcessNextBatchAsync_KeepsPersistedCheckpointWhenLaterEventFails()
    {
        var db = BuildDbContext();
        var subscription = new ThrowingSubscription(throwOnAttempt: 2);
        var provider = BuildProvider(db, subscription);
        var lockProvider = new FakeLockProvider();
        var options = Options.Create(new SubscriptionOptions
        {
            BatchSize = 3,
            CheckpointFrequency = 1,
            RetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryAttempts = 3
        });

        db.Streams.StartStream(Guid.NewGuid(), events: [new object(), new object(), new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequencesAsync(db);

        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            options);

        var processed = await daemon.ProcessNextBatchAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);
        var subscriptionEntity = await FindSubscriptionAsync(db, subscription.GetType().AssemblyQualifiedName!, CheckpointScope.Global, Guid.Empty);

        Assert.Equal(1, processed);
        Assert.Equal(2, subscription.HandledCount);
        Assert.NotNull(subscriptionEntity);
        Assert.Equal(SubscriptionState.Faulted, subscriptionEntity!.State);
        Assert.Equal(1, subscriptionEntity.Sequence);
        Assert.Equal(2, subscriptionEntity.FailedEventSequence);
    }

    [Fact]
    public async Task AcquireSubscriptionLockAsync_UsesConfiguredTimeout()
    {
        var db = BuildDbContext();
        var subscription = new RecordingSubscription();
        var provider = BuildProvider(db, subscription);
        var lockProvider = new FakeLockProvider();
        var configuredTimeout = TimeSpan.FromSeconds(17);
        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new SubscriptionOptions { LockTimeout = configuredTimeout }));

        await using var handle = await daemon.AcquireSubscriptionLockAsync<RecordingSubscription>(
            TestContext.Current.CancellationToken);

        Assert.Equal(configuredTimeout, lockProvider.LastAcquireTimeout);
    }

    [Fact]
    public async Task ProcessNextBatchAsync_RollsBackFailingScopedHandlerMutations()
    {
        var db = BuildDbContext();
        var subscription = new MutatingThrowingScopedSubscription();
        var provider = BuildProvider(db, subscription);
        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new SubscriptionOptions()));

        db.Streams.StartStream(Guid.NewGuid(), events: [new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequencesAsync(db);

        var processed = await daemon.ProcessNextBatchAsync(
            provider.CreateScope(),
            subscription,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, processed);
        Assert.False(await db.Set<DbProjectionStatus>()
            .AnyAsync(x => x.ProjectionName == "must-not-commit", TestContext.Current.CancellationToken));
        var status = await FindSubscriptionAsync(
            db,
            subscription.GetType().AssemblyQualifiedName!,
            CheckpointScope.Global,
            Guid.Empty);
        Assert.Equal(SubscriptionState.Faulted, status!.State);
    }

    [Fact]
    public async Task ProcessNextEventAsync_FaultsSubscriptionAndSchedulesRetry()
    {
        var db = BuildDbContext();
        var subscription = new ThrowingSubscription();
        var provider = BuildProvider(db, subscription);
        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new SubscriptionOptions
            {
                RetryDelay = TimeSpan.FromSeconds(5),
                MaxRetryAttempts = 3
            }));

        db.Streams.StartStream(Guid.NewGuid(), events: [new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequenceAsync(db);

        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);
        var status = await FindSubscriptionAsync(db, subscription.GetType().AssemblyQualifiedName!, CheckpointScope.Global, Guid.Empty);

        Assert.False(processed);
        Assert.NotNull(status);
        Assert.Equal(SubscriptionState.Faulted, status!.State);
        Assert.Equal(1, status.AttemptCount);
        Assert.NotNull(status.LastError);
        Assert.NotNull(status.LastAttemptAt);
        Assert.NotNull(status.NextAttemptAt);
        Assert.NotNull(status.FailedEventSequence);
    }

    [Fact]
    public async Task ProcessNextEventAsync_DeadLettersSubscription_WhenMaxRetryAttemptsReached()
    {
        var db = BuildDbContext();
        var subscription = new ThrowingSubscription();
        var provider = BuildProvider(db, subscription);
        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new SubscriptionOptions
            {
                RetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryAttempts = 1
            }));

        db.Streams.StartStream(Guid.NewGuid(), events: [new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequenceAsync(db);

        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);
        var status = await FindSubscriptionAsync(db, subscription.GetType().AssemblyQualifiedName!, CheckpointScope.Global, Guid.Empty);

        Assert.False(processed);
        Assert.NotNull(status);
        Assert.Equal(SubscriptionState.DeadLettered, status!.State);
        Assert.Equal(1, status.AttemptCount);
    }

    [Fact]
    public async Task ProcessNextEventAsync_DoesNotProcessPausedSubscription()
    {
        var db = BuildDbContext();
        var subscription = new RecordingSubscription();
        var provider = BuildProvider(db, subscription);
        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new SubscriptionOptions()));

        db.Streams.StartStream(Guid.NewGuid(), events: [new object()]);
        db.Set<DbSubscription>().Add(new DbSubscription
        {
            SubscriptionAssemblyQualifiedName = subscription.GetType().AssemblyQualifiedName!,
            State = SubscriptionState.Paused
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await EnsureSequenceAsync(db);

        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        Assert.False(processed);
        Assert.False(subscription.Handled);
    }

    [Fact]
    public async Task ProcessNextBatchAsync_TenantScopedCheckpoint_IsolatesPoisonTenant()
    {
        var db = BuildDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var subscription = new TenantPoisonSubscription(tenantA);
        var provider = BuildProvider(db, subscription);
        var daemon = new SubscriptionDaemon<ExecutionDbContext>(
            NullLogger<SubscriptionDaemon<ExecutionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new SubscriptionOptions
            {
                CheckpointScope = CheckpointScope.Tenant,
                MaxRetryAttempts = 1,
                RetryDelay = TimeSpan.FromSeconds(5)
            }));

        db.Set<DbEvent>().AddRange(
            CreateEvent(tenantA, 1),
            CreateEvent(tenantB, 2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var processedA = await daemon.ProcessNextBatchAsync(
            provider.CreateScope(),
            subscription,
            TestContext.Current.CancellationToken,
            tenantA);
        var processedB = await daemon.ProcessNextBatchAsync(
            provider.CreateScope(),
            subscription,
            TestContext.Current.CancellationToken,
            tenantB);

        var name = subscription.GetType().AssemblyQualifiedName!;
        var tenantAStatus = await FindSubscriptionAsync(db, name, CheckpointScope.Tenant, tenantA);
        var tenantBStatus = await FindSubscriptionAsync(db, name, CheckpointScope.Tenant, tenantB);

        Assert.Equal(0, processedA);
        Assert.Equal(1, processedB);
        Assert.NotNull(tenantAStatus);
        Assert.NotNull(tenantBStatus);
        Assert.Equal(SubscriptionState.DeadLettered, tenantAStatus!.State);
        Assert.Equal(1, tenantAStatus.FailedEventSequence);
        Assert.Equal(SubscriptionState.Active, tenantBStatus!.State);
        Assert.Equal(2, tenantBStatus.Sequence);
        Assert.Contains(tenantB, subscription.HandledTenantIds);
    }

    private static async Task EnsureSequenceAsync(ExecutionDbContext db)
    {
        var storedEvent = await db.Set<DbEvent>().FirstAsync(TestContext.Current.CancellationToken);
        if (storedEvent.Sequence == 0)
        {
            storedEvent.Sequence = 1;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task EnsureSequencesAsync(ExecutionDbContext db)
    {
        var events = await db.Set<DbEvent>()
            .OrderBy(e => e.Version)
            .ToListAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < events.Count; i++)
        {
            events[i].Sequence = i + 1;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static CountingExecutionDbContext BuildCountingDbContext()
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new CountingExecutionDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<DbSubscription?> FindSubscriptionAsync(
        ExecutionDbContext db,
        string name,
        CheckpointScope checkpointScope,
        Guid tenantId)
    {
        return await db.Set<DbSubscription>()
            .FirstOrDefaultAsync(s =>
                s.SubscriptionAssemblyQualifiedName == name &&
                s.CheckpointScope == checkpointScope &&
                s.TenantId == tenantId,
                TestContext.Current.CancellationToken);
    }

    private static DbEvent CreateEvent(Guid tenantId, long sequence)
    {
        return new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = tenantId,
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = sequence,
            Type = typeof(object).AssemblyQualifiedName!,
            Data = "{}"
        };
    }
}
