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

    private sealed class FakeLockProvider : IDistributedLockProvider
    {
        public bool ReturnNullHandle { get; set; }

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

    private static ServiceProvider BuildProvider<TDbContext>(TDbContext db, RecordingSubscription subscription)
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

        var storedEvent = await db.Events.FirstAsync(TestContext.Current.CancellationToken);
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

        var subscriptionEntity = await db.Set<DbSubscription>()
            .FindAsync([subscription.GetType().AssemblyQualifiedName!], TestContext.Current.CancellationToken);

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

    private static async Task EnsureSequencesAsync(ExecutionDbContext db)
    {
        var events = await db.Events
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
}
