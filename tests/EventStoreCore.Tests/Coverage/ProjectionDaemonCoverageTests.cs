using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EventStoreCore.Tests;

public class ProjectionDaemonCoverageTests
{
    private sealed class ProjectionEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ProjectionSnapshot
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ProjectionDbContext : DbContext
    {
        public ProjectionDbContext(DbContextOptions<ProjectionDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
            modelBuilder.Entity<ProjectionSnapshot>().HasKey(x => x.Id);
        }
    }

    private static ProjectionRegistration BuildProjectionRegistration(int version, ProjectionOptions options)
    {
        return new ProjectionRegistration
        {
            Name = "Projection",
            Mode = ProjectionMode.Eventual,
            Version = version,
            ProjectionType = typeof(ProjectionSnapshot),
            SnapshotType = typeof(ProjectionSnapshot),
            Options = options,
            ClearAction = async (db, _, ct) =>
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
                var entity = (ProjectionSnapshot)snapshot;
                if (@event is IEvent<ProjectionEvent> evt)
                {
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

    private static ProjectionDaemon<ProjectionDbContext> BuildDaemon(IServiceProvider provider, ProjectionDaemonOptions options, params ProjectionRegistration[] registrations)
    {
        var services = new ServiceCollection();
        foreach (var registration in registrations)
        {
            services.AddSingleton(registration);
        }
        foreach (var service in provider.GetServices<ProjectionDbContext>())
        {
            services.AddSingleton(service);
        }

        var sp = provider;
        return new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            sp,
            new FakeLockProvider(),
            Options.Create(options));
    }

    private static ProjectionDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ProjectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ProjectionDbContext(options);
    }

    [Fact]
    public async Task InitiateRebuildAsync_UpdatesStatusAndClearsSnapshots()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var options = new ProjectionOptions();
        options.Handles<ProjectionEvent>();
        var registration = BuildProjectionRegistration(2, options);

        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 5
        };
        db.Set<DbProjectionStatus>().Add(status);
        db.Set<ProjectionSnapshot>().Add(new ProjectionSnapshot { Id = Guid.NewGuid(), Name = "Old" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            Substitute.For<IDistributedLockProvider>(),
            Options.Create(new ProjectionDaemonOptions()));

        var projectionSet = db.Set<ProjectionSnapshot>();
        foreach (var snapshot in projectionSet)
        {
            projectionSet.Remove(snapshot);
        }

        await daemon.InitiateRebuildAsync(
            db,
            provider,
            registration,
            status,
            TestContext.Current.CancellationToken);

        var updated = await db.Set<DbProjectionStatus>().SingleAsync(TestContext.Current.CancellationToken);
        var snapshotCount = await db.Set<ProjectionSnapshot>().CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProjectionState.Rebuilding, updated.State);
        Assert.Equal(0, updated.Position);
        Assert.Equal(2, updated.Version);
        Assert.Equal(0, snapshotCount);
    }

    [Fact]
    public async Task ProcessBatchAsync_FaultsProjectionOnFailure()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var options = new ProjectionOptions();
        options.Handles<ProjectionEvent>();

        var registration = new ProjectionRegistration
        {
            Name = "Projection",
            Mode = ProjectionMode.Eventual,
            Version = 1,
            ProjectionType = typeof(ProjectionSnapshot),
            SnapshotType = typeof(ProjectionSnapshot),
            Options = options,
            ClearAction = (_, _, _) => Task.CompletedTask,
            EvolveAction = (_, _, _, _, _) => throw new InvalidOperationException("boom"),
            GetOrCreateSnapshotAction = (_, _, _) => Task.FromResult<object>(new ProjectionSnapshot()),
            AddSnapshotAction = (_, _) => { }
        };

        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        };
        db.Set<DbProjectionStatus>().Add(status);

        var dbEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Test\"}"
        };
        db.Set<DbEvent>().Add(dbEvent);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        var lockProvider = new FakeLockProvider();

        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            lockProvider,
            Options.Create(new ProjectionDaemonOptions()));

        var processBatchMethod = typeof(ProjectionDaemon<ProjectionDbContext>)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(processBatchMethod);

        var task = (Task)processBatchMethod!.Invoke(daemon, new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);

        var updated = await db.Set<DbProjectionStatus>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
    }

    [Fact]
    public async Task ProcessBatchAsync_SkipsUnresolvableEvent_WhenHandlesUsed()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var options = new ProjectionOptions();
        options.Handles<ProjectionEvent>();

        var evolved = false;
        var registration = new ProjectionRegistration
        {
            Name = "Projection",
            Mode = ProjectionMode.Eventual,
            Version = 1,
            ProjectionType = typeof(ProjectionSnapshot),
            SnapshotType = typeof(ProjectionSnapshot),
            Options = options,
            ClearAction = (_, _, _) => Task.CompletedTask,
            EvolveAction = (_, _, snapshot, @event, _) =>
            {
                evolved = true;
                var entity = (ProjectionSnapshot)snapshot;
                if (@event is IEvent<ProjectionEvent> evt)
                {
                    entity.Id = evt.StreamId;
                    entity.Name = evt.Data.Name;
                }
                return Task.CompletedTask;
            },
            GetOrCreateSnapshotAction = (_, _, _) => Task.FromResult<object>(new ProjectionSnapshot()),
            AddSnapshotAction = (db, snapshot) => db.Add(snapshot)
        };

        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        };
        db.Set<DbProjectionStatus>().Add(status);

        var streamId = Guid.NewGuid();

        // Unresolvable event (CLR type no longer exists)
        var unknownEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = streamId,
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = "NonExistent.RemovedEvent, NonExistent",
            TypeName = "removed_event",
            Data = "{}"
        };

        // Resolvable and handled event
        var knownEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = streamId,
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 2,
            Sequence = 2,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Test\"}"
        };

        db.Set<DbEvent>().AddRange(unknownEvent, knownEvent);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new ProjectionDaemonOptions()));

        var processBatchMethod = typeof(ProjectionDaemon<ProjectionDbContext>)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var task = (Task<bool>)processBatchMethod.Invoke(daemon, new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        var result = await task;

        Assert.True(result);
        Assert.True(evolved);
        Assert.Equal(2, status.Position);
    }

    [Fact]
    public async Task ProcessBatchAsync_FiltersBeforeBatchLimitAndAdvancesThroughTrailingFilteredEvents()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var tenantId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        var options = new ProjectionOptions();
        options.Handles<ProjectionEvent>();
        options.IncludeLogicalEventType("projection_event");
        options.IncludeStreamType("orders");
        options.IncludeStream(streamId);
        options.IncludeTenant(tenantId);

        var evolvedSequences = new List<long>();
        var registration = BuildProjectionRegistration(1, options);
        registration = new ProjectionRegistration
        {
            Name = registration.Name,
            Mode = registration.Mode,
            Version = registration.Version,
            ProjectionType = registration.ProjectionType,
            SnapshotType = registration.SnapshotType,
            Options = registration.Options,
            ClearAction = registration.ClearAction,
            EvolveAction = (_, _, snapshot, @event, _) =>
            {
                evolvedSequences.Add(@event.Sequence);
                var entity = (ProjectionSnapshot)snapshot;
                entity.Id = @event.StreamId;
                return Task.CompletedTask;
            },
            GetOrCreateSnapshotAction = registration.GetOrCreateSnapshotAction,
            AddSnapshotAction = registration.AddSnapshotAction
        };

        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        };
        db.Set<DbProjectionStatus>().Add(status);
        db.Set<DbEvent>().AddRange(
            CreateEvent(1, "other_event", "orders", streamId, tenantId),
            CreateEvent(2, "projection_event", "orders", streamId, tenantId),
            CreateEvent(3, "projection_event", "audit", streamId, tenantId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new ProjectionDaemonOptions { BatchSize = 1 }));
        var processBatchMethod = typeof(ProjectionDaemon<ProjectionDbContext>)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var first = (Task<bool>)processBatchMethod.Invoke(
            daemon,
            new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        Assert.True(await first);
        Assert.Equal([2], evolvedSequences);
        Assert.Equal(2, status.Position);

        var second = (Task<bool>)processBatchMethod.Invoke(
            daemon,
            new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        Assert.True(await second);
        Assert.Equal(3, status.Position);

        var third = (Task<bool>)processBatchMethod.Invoke(
            daemon,
            new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        Assert.False(await third);
    }

    [Fact]
    public async Task ProcessBatchAsync_DoesNotMaterializeUnknownEventsExcludedByPersistedFilter()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var options = new ProjectionOptions();
        options.HandlesAll();
        options.IncludeLogicalEventType("projection_event");
        var registration = BuildProjectionRegistration(1, options);
        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        };
        db.Set<DbProjectionStatus>().Add(status);
        db.Set<DbEvent>().Add(new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = "NonExistent.RemovedEvent, NonExistent",
            TypeName = "removed_event",
            Data = "{}"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new ProjectionDaemonOptions { BatchSize = 1 }));
        var processBatchMethod = typeof(ProjectionDaemon<ProjectionDbContext>)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var task = (Task<bool>)processBatchMethod.Invoke(
            daemon,
            new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;

        Assert.True(await task);
        Assert.Equal(1, status.Position);
    }

    [Fact]
    public async Task ProcessBatchAsync_ThrowsForUnresolvableEvent_WhenHandlesAll()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var options = new ProjectionOptions();
        // Default is HandlesAll — should throw on unresolvable
        options.IncludeLogicalEventType("removed_event");

        var registration = BuildProjectionRegistration(1, options);

        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        };
        db.Set<DbProjectionStatus>().Add(status);

        var unknownEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = "NonExistent.RemovedEvent, NonExistent",
            TypeName = "removed_event",
            Data = "{}"
        };
        db.Set<DbEvent>().Add(unknownEvent);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new ProjectionDaemonOptions()));

        var processBatchMethod = typeof(ProjectionDaemon<ProjectionDbContext>)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var task = (Task)processBatchMethod.Invoke(daemon, new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        await Assert.ThrowsAsync<EventMaterializationException>(async () => await task);
    }

    private static DbEvent CreateEvent(
        long sequence,
        string logicalEventType,
        string streamType,
        Guid streamId,
        Guid tenantId) =>
        new()
        {
            EventId = Guid.NewGuid(),
            StreamId = streamId,
            StreamType = streamType,
            TenantId = tenantId,
            Timestamp = DateTimeOffset.UtcNow,
            Version = sequence,
            Sequence = sequence,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            TypeName = logicalEventType,
            Data = "{\"Name\":\"Test\"}"
        };

    [Fact]
    public async Task ProcessBatchAsync_SkipsUnresolvableEvent_WhenHandlesAllWithIgnoreUnknown()
    {
        var db = BuildDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var options = new ProjectionOptions();
        options.HandlesAll();
        options.IgnoreUnknown();

        var evolved = false;
        var registration = new ProjectionRegistration
        {
            Name = "Projection",
            Mode = ProjectionMode.Eventual,
            Version = 1,
            ProjectionType = typeof(ProjectionSnapshot),
            SnapshotType = typeof(ProjectionSnapshot),
            Options = options,
            ClearAction = (_, _, _) => Task.CompletedTask,
            EvolveAction = (_, _, snapshot, @event, _) =>
            {
                evolved = true;
                var entity = (ProjectionSnapshot)snapshot;
                if (@event is IEvent<ProjectionEvent> evt)
                {
                    entity.Id = evt.StreamId;
                    entity.Name = evt.Data.Name;
                }
                return Task.CompletedTask;
            },
            GetOrCreateSnapshotAction = (_, _, _) => Task.FromResult<object>(new ProjectionSnapshot()),
            AddSnapshotAction = (db, snapshot) => db.Add(snapshot)
        };

        var status = new DbProjectionStatus
        {
            ProjectionName = registration.Name,
            Version = 1,
            State = ProjectionState.Active,
            Position = 0
        };
        db.Set<DbProjectionStatus>().Add(status);

        var streamId = Guid.NewGuid();

        var unknownEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = streamId,
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 1,
            Sequence = 1,
            Type = "NonExistent.RemovedEvent, NonExistent",
            TypeName = "removed_event",
            Data = "{}"
        };

        var knownEvent = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = streamId,
            TenantId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 2,
            Sequence = 2,
            Type = typeof(ProjectionEvent).AssemblyQualifiedName!,
            Data = "{\"Name\":\"Test\"}"
        };

        db.Set<DbEvent>().AddRange(unknownEvent, knownEvent);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        var daemon = new ProjectionDaemon<ProjectionDbContext>(
            NullLogger<ProjectionDaemon<ProjectionDbContext>>.Instance,
            provider,
            new FakeLockProvider(),
            Options.Create(new ProjectionDaemonOptions()));

        var processBatchMethod = typeof(ProjectionDaemon<ProjectionDbContext>)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var task = (Task<bool>)processBatchMethod.Invoke(daemon, new object[] { db, provider, registration, status, TestContext.Current.CancellationToken })!;
        var result = await task;

        Assert.True(result);
        Assert.True(evolved);
        Assert.Equal(2, status.Position);
    }
}
