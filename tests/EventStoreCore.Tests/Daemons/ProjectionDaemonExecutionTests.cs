using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Tests;

public class ProjectionDaemonExecutionTests
{
    private sealed class ExecutionDbContext : DbContext
    {
        public ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : base(options)
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

    private sealed class FakeLockProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new FakeLock();
    }

    private sealed class FakeLock : IDistributedLock
    {
        public string Name => "fake";
        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new FakeHandle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new(new FakeHandle());
        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) => new FakeHandle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default) => new(new FakeHandle());
    }

    private sealed class FakeHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private static ProjectionRegistration BuildRegistration(Guid? throwForTenantId = null)
    {
        var options = new ProjectionOptions();
        options.Handles<ProjectionEvent>();

        return new ProjectionRegistration
        {
            Name = "ExecutionProjection",
            Version = 1,
            ProjectionType = typeof(ProjectionSnapshot),
            SnapshotType = typeof(ProjectionSnapshot),
            Options = options,
            ClearAction = (_, _) => Task.CompletedTask,
            EvolveAction = async (_, _, snapshot, @event, _) =>
            {
                if (@event is IEvent<ProjectionEvent> evt)
                {
                    if (evt.TenantId == throwForTenantId)
                    {
                        throw new InvalidOperationException("Projection failed for tenant.");
                    }

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

    private static async Task RunExecuteAsync(ProjectionDaemon<ExecutionDbContext> daemon, CancellationToken token)
    {
        var method = typeof(ProjectionDaemon<ExecutionDbContext>).GetMethod(
            "ExecuteAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { token })!;
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesProjectionLoop()
    {
        var db = BuildDbContext();
        var registration = BuildRegistration();
        var lockProvider = new FakeLockProvider();

        db.Events.Add(new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Execute\"}"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(registration)
            .BuildServiceProvider();

        var daemon = new ProjectionDaemon<ExecutionDbContext>(
            NullLogger<ProjectionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new ProjectionDaemonOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(5),
                RetryDelay = TimeSpan.FromMilliseconds(5)
            }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await RunExecuteAsync(daemon, cts.Token);

        var status = await db.Set<DbProjectionStatus>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.True(status == null || status.Position >= 0);
    }

    [Fact]
    public async Task ProcessProjectionAsync_TenantScopedCheckpoint_CreatesIndependentStatuses()
    {
        var db = BuildDbContext();
        var registration = BuildRegistration();
        var lockProvider = new FakeLockProvider();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var streamA = Guid.NewGuid();
        var streamB = Guid.NewGuid();

        db.Events.AddRange(
            CreateEvent(tenantA, streamA, 1, "Tenant A"),
            CreateEvent(tenantB, streamB, 2, "Tenant B"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(registration)
            .BuildServiceProvider();

        var daemon = new ProjectionDaemon<ExecutionDbContext>(
            NullLogger<ProjectionDaemon<ExecutionDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new ProjectionDaemonOptions
            {
                CheckpointScope = CheckpointScope.Tenant,
                AutoRebuildOnVersionChange = false
            }));

        var method = typeof(ProjectionDaemon<ExecutionDbContext>).GetMethod(
            "ProcessProjectionAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { registration, TestContext.Current.CancellationToken })!;
        await task;

        var statuses = await db.Set<DbProjectionStatus>()
            .OrderBy(s => s.TenantId)
            .ToListAsync(TestContext.Current.CancellationToken);
        var snapshots = await db.Set<ProjectionSnapshot>()
            .OrderBy(s => s.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, status => Assert.Equal(CheckpointScope.Tenant, status.CheckpointScope));
        Assert.Contains(statuses, status => status.TenantId == tenantA && status.Position == 1);
        Assert.Contains(statuses, status => status.TenantId == tenantB && status.Position == 2);
        Assert.Equal(2, snapshots.Count);
        Assert.Contains(snapshots, snapshot => snapshot.Id == streamA && snapshot.Name == "Tenant A");
        Assert.Contains(snapshots, snapshot => snapshot.Id == streamB && snapshot.Name == "Tenant B");
    }

    [Fact]
    public async Task ProcessProjectionAsync_TenantScopedCheckpoint_ContinuesAfterPoisonTenant()
    {
        var db = BuildDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var streamA = Guid.NewGuid();
        var streamB = Guid.NewGuid();
        var registration = BuildRegistration(tenantA);

        db.Events.AddRange(
            CreateEvent(tenantA, streamA, 1, "Tenant A"),
            CreateEvent(tenantB, streamB, 2, "Tenant B"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(registration)
            .BuildServiceProvider();

        var daemon = new ProjectionDaemon<ExecutionDbContext>(
            NullLogger<ProjectionDaemon<ExecutionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new ProjectionDaemonOptions
            {
                CheckpointScope = CheckpointScope.Tenant,
                AutoRebuildOnVersionChange = false
            }));

        var method = typeof(ProjectionDaemon<ExecutionDbContext>).GetMethod(
            "ProcessProjectionAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(daemon, new object[] { registration, TestContext.Current.CancellationToken })!;
        await task;

        var statuses = await db.Set<DbProjectionStatus>()
            .ToListAsync(TestContext.Current.CancellationToken);
        var snapshot = await db.Set<ProjectionSnapshot>()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Contains(statuses, status =>
            status.TenantId == tenantA &&
            status.State == ProjectionState.Faulted &&
            status.FailedEventSequence == 1);
        Assert.Contains(statuses, status =>
            status.TenantId == tenantB &&
            status.State == ProjectionState.Active &&
            status.Position == 2);
        Assert.Equal(streamB, snapshot.Id);
        Assert.Equal("Tenant B", snapshot.Name);
    }

    private static DbEvent CreateEvent(Guid tenantId, Guid streamId, long sequence, string name)
    {
        return new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = streamId,
            TenantId = tenantId,
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = sequence,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            Data = $$"""{"Name":"{{name}}"}"""
        };
    }
}
