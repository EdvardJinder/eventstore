using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Tests;

public class DaemonHarnessTests
{
    private sealed class HarnessDbContext : DbContext
    {
        public HarnessDbContext(DbContextOptions<HarnessDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
            modelBuilder.Entity<ProjectionSnapshot>().HasKey(x => x.Id);
        }
    }

    private sealed class ProjectionEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ProjectionSnapshot
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ScopedSubscription : IScopedSubscription
    {
        public bool Handled { get; private set; }

        public Task Handle(IEvent @event, CancellationToken ct) => Task.CompletedTask;

        public Task HandleAsync(DbContext dbContext, IEvent @event, CancellationToken ct)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class BasicSubscription : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeLockProvider : IDistributedLockProvider
    {
        public bool AcquireThrowsTimeout { get; set; }
        public bool TryAcquireReturnsNull { get; set; }

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

        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (_provider.AcquireThrowsTimeout)
            {
                throw new TimeoutException();
            }
            return new FakeLockHandle();
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (_provider.AcquireThrowsTimeout)
            {
                throw new TimeoutException();
            }
            return new ValueTask<IDistributedSynchronizationHandle>(new FakeLockHandle());
        }

        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            return _provider.TryAcquireReturnsNull ? null : new FakeLockHandle();
        }

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            return new ValueTask<IDistributedSynchronizationHandle?>(_provider.TryAcquireReturnsNull ? null : new FakeLockHandle());
        }
    }

    private sealed class FakeLockHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ServiceProvider BuildProvider(Action<ServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static HarnessDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<HarnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new HarnessDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static ProjectionRegistration BuildProjectionRegistration(int version)
    {
        var options = new ProjectionOptions();
        options.Handles<ProjectionEvent>();

        return new ProjectionRegistration
        {
            Name = "Projection",
            Version = version,
            ProjectionType = typeof(ProjectionSnapshot),
            SnapshotType = typeof(ProjectionSnapshot),
            Options = options,
            ClearAction = async (db, ct) =>
            {
                var set = db.Set<ProjectionSnapshot>();
                foreach (var snapshot in set)
                {
                    set.Remove(snapshot);
                }
                await db.SaveChangesAsync(ct);
            },
            EvolveAction = async (db, sp, snapshot, @event, ct) =>
            {
                if (@event is IEvent<ProjectionEvent> evt)
                {
                    var entity = (ProjectionSnapshot)snapshot;
                    entity.Id = evt.StreamId;
                    entity.Name = evt.Data.Name;
                }
                await Task.CompletedTask;
            },
            GetOrCreateSnapshotAction = async (db, key, ct) =>
            {
                var id = (Guid)key;
                var existing = await db.Set<ProjectionSnapshot>().FindAsync([id], ct);
                return existing ?? new ProjectionSnapshot { Id = id };
            },
            AddSnapshotAction = (db, snapshot) => db.Add(snapshot)
        };
    }

    [Fact]
    public async Task SubscriptionDaemon_ProcessNextEventAsync_HandlesScopedSubscriptions()
    {
        var db = BuildDbContext();
        var subscription = new ScopedSubscription();
        var services = BuildProvider(svc =>
        {
            svc.AddSingleton(db);
        });

        db.Streams.StartStream(Guid.NewGuid(), events: [new ProjectionEvent { Name = "one" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var storedEvent = await db.Events.FirstAsync(TestContext.Current.CancellationToken);
        if (storedEvent.Sequence == 0)
        {
            storedEvent.Sequence = 1;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var daemon = new SubscriptionDaemon<HarnessDbContext>(
            NullLogger<SubscriptionDaemon<HarnessDbContext>>.Instance,
            services,
            new FakeLockProvider(),
            Options.Create(new SubscriptionOptions()));

        var method = typeof(SubscriptionDaemon<HarnessDbContext>).GetMethod(
            "ProcessNextEventAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        Assert.NotNull(method);

        var scope = services.CreateScope();
        var task = (Task<bool>)method!.Invoke(daemon, new object[] { scope, subscription, TestContext.Current.CancellationToken })!;
        var processed = await task;

        Assert.True(processed);
        Assert.True(subscription.Handled);
    }

    [Fact]
    public async Task SubscriptionDaemon_AcquireSubscriptionLockAsync_ReturnsNullOnTimeout()
    {
        var services = BuildProvider(_ => { });
        var lockProvider = new FakeLockProvider { AcquireThrowsTimeout = true };
        var daemon = new SubscriptionDaemon<HarnessDbContext>(
            NullLogger<SubscriptionDaemon<HarnessDbContext>>.Instance,
            services,
            lockProvider,
            Options.Create(new SubscriptionOptions()));

        var method = typeof(SubscriptionDaemon<HarnessDbContext>).GetMethod(
            "AcquireSubscriptionLockAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            new[] { typeof(Type), typeof(CancellationToken) },
            null);

        Assert.NotNull(method);

        var task = (Task<IAsyncDisposable?>)method!.Invoke(daemon, new object[] { typeof(BasicSubscription), TestContext.Current.CancellationToken })!;
        var handle = await task;

        Assert.Null(handle);
    }

    [Fact]
    public async Task ProjectionDaemon_ProcessProjectionAsync_TriggersRebuildOnVersionChange()
    {
        var db = BuildDbContext();
        var registration = BuildProjectionRegistration(version: 2);
        var lockProvider = new FakeLockProvider();

        db.Set<ProjectionSnapshot>().Add(new ProjectionSnapshot { Id = Guid.NewGuid(), Name = "old" });
        db.Set<DbProjectionStatus>().Add(new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = BuildProvider(services =>
        {
            services.AddSingleton(registration);
            services.AddSingleton(db);
        });

        var daemon = new ProjectionDaemon<HarnessDbContext>(
            NullLogger<ProjectionDaemon<HarnessDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new ProjectionDaemonOptions()));

        var method = typeof(ProjectionDaemon<HarnessDbContext>).GetMethod(
            "ProcessProjectionAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { registration, TestContext.Current.CancellationToken })!;
        await task;

        var status = await db.Set<DbProjectionStatus>().SingleAsync(TestContext.Current.CancellationToken);
        var snapshots = await db.Set<ProjectionSnapshot>().ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, status.Version);
        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task ProjectionDaemon_ProcessProjectionAsync_CompletesRebuildWhenNoEvents()
    {
        var db = BuildDbContext();
        var registration = BuildProjectionRegistration(version: 1);
        var lockProvider = new FakeLockProvider();

        db.Set<DbProjectionStatus>().Add(new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Rebuilding,
            Position = 0,
            RebuildStartedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = BuildProvider(services =>
        {
            services.AddSingleton(registration);
            services.AddSingleton(db);
        });

        var daemon = new ProjectionDaemon<HarnessDbContext>(
            NullLogger<ProjectionDaemon<HarnessDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new ProjectionDaemonOptions()));

        var method = typeof(ProjectionDaemon<HarnessDbContext>).GetMethod(
            "ProcessProjectionAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { registration, TestContext.Current.CancellationToken })!;
        await task;

        var status = await db.Set<DbProjectionStatus>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProjectionState.Active, status.State);
        Assert.NotNull(status.RebuildCompletedAt);
    }

    [Fact]
    public async Task ProjectionDaemon_ProcessProjectionAsync_ProcessesActiveProjection()
    {
        var db = BuildDbContext();
        var registration = BuildProjectionRegistration(version: 1);
        var lockProvider = new FakeLockProvider();

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Active\"}"
        };
        db.Events.Add(dbEvent);
        db.Set<DbProjectionStatus>().Add(new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = BuildProvider(services =>
        {
            services.AddSingleton(registration);
            services.AddSingleton(db);
        });

        var daemon = new ProjectionDaemon<HarnessDbContext>(
            NullLogger<ProjectionDaemon<HarnessDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new ProjectionDaemonOptions()));

        var method = typeof(ProjectionDaemon<HarnessDbContext>).GetMethod(
            "ProcessProjectionAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { registration, TestContext.Current.CancellationToken })!;
        await task;

        var status = await db.Set<DbProjectionStatus>().SingleAsync(TestContext.Current.CancellationToken);
        var snapshot = await db.Set<ProjectionSnapshot>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, status.Position);
        Assert.Equal(dbEvent.StreamId, snapshot.Id);
        Assert.Equal("Active", snapshot.Name);
    }
}
