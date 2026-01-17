using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EventStoreCore.Tests;

public class SubscriptionManagerCoverageTests
{
    private sealed class SampleSubscription : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CoverageDbContext : DbContext
    {
        public CoverageDbContext(DbContextOptions<CoverageDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
        }
    }

    private static CoverageDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<CoverageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new CoverageDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static SubscriptionManager<CoverageDbContext> BuildManager(CoverageDbContext db, IEnumerable<ISubscription> subscriptions)
    {
        var lockProvider = new FakeLockProvider();
        return (SubscriptionManager<CoverageDbContext>)Activator.CreateInstance(
            typeof(SubscriptionManager<CoverageDbContext>),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { db, lockProvider, subscriptions, NullLogger<SubscriptionManager<CoverageDbContext>>.Instance },
            null)!;
    }

    private sealed class FakeLockProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new FakeLock();
    }

    private sealed class FakeLock : IDistributedLock
    {
        public string Name => "fake";
        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new FakeLockHandle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new(new FakeLockHandle());
        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) => new FakeLockHandle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default) => new(new FakeLockHandle());
    }

    private sealed class FakeLockHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetAllStatusesAsync_ReturnsEmptyWhenNoSubscriptionsRegistered()
    {
        var db = BuildDbContext();
        var manager = BuildManager(db, Array.Empty<ISubscription>());

        var statuses = await manager.GetAllStatusesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(statuses);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNullForUnknownSubscription()
    {
        var db = BuildDbContext();
        var manager = BuildManager(db, Array.Empty<ISubscription>());

        var status = await manager.GetStatusAsync("missing", TestContext.Current.CancellationToken);

        Assert.Null(status);
    }

    [Fact]
    public async Task ReplayAsync_ThrowsWhenBothStartAndTimestampProvided()
    {
        var db = BuildDbContext();
        var manager = BuildManager(db, new[] { new SampleSubscription() });
        var name = typeof(SampleSubscription).AssemblyQualifiedName!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReplayAsync(name, 1, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));

        Assert.Contains("either startSequence or fromTimestamp", exception.Message);
    }

    [Fact]
    public async Task ReplayAsync_ThrowsWhenSubscriptionNotRegistered()
    {
        var db = BuildDbContext();
        var manager = BuildManager(db, Array.Empty<ISubscription>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReplayAsync("missing", ct: TestContext.Current.CancellationToken));

        Assert.Contains("not registered", exception.Message);
    }

    [Fact]
    public async Task GetAllStatusesAsync_ReturnsDefaultsForMissingRecords()
    {
        var db = BuildDbContext();
        var subscription = new SampleSubscription();
        var manager = BuildManager(db, new[] { subscription });

        var statuses = await manager.GetAllStatusesAsync(TestContext.Current.CancellationToken);

        Assert.Single(statuses);
        Assert.Equal(typeof(SampleSubscription).AssemblyQualifiedName, statuses[0].SubscriptionName);
        Assert.Equal(0, statuses[0].Position);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsLastProcessedTimestamp()
    {
        var db = BuildDbContext();
        var subscription = new SampleSubscription();
        var manager = BuildManager(db, new[] { subscription });
        var name = typeof(SampleSubscription).AssemblyQualifiedName!;

        var streamId = Guid.NewGuid();
        db.Streams.StartStream(streamId, events: [new object()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trackedEvent = await db.Events
            .OrderBy(e => e.Sequence)
            .FirstAsync(TestContext.Current.CancellationToken);
        trackedEvent.Timestamp = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var record = new DbSubscription
        {
            SubscriptionAssemblyQualifiedName = name,
            Sequence = trackedEvent.Sequence
        };
        db.Set<DbSubscription>().Add(record);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var status = await manager.GetStatusAsync(name, TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(trackedEvent.Sequence, status!.Position);
    }
}
