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
    private sealed class ExecutionDbContext : DbContext
    {
        public ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
        }
    }

    private sealed class RecordingSubscription : ISubscription
    {
        public bool Handled { get; private set; }
        public Task Handle(IEvent @event, CancellationToken ct)
        {
            Handled = true;
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

    private static ServiceProvider BuildProvider(ExecutionDbContext db, RecordingSubscription subscription)
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
}
