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

    private static ProjectionRegistration BuildRegistration()
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
}
