using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Tests;

public sealed class DaemonSchedulingIsolationTests
{
    [Fact]
    public async Task Slow_stream_subscription_does_not_block_another_registration()
    {
        var slowEntered = NewSignal();
        var releaseSlow = NewSignal();
        var fastHandled = NewSignal();
        var slow = new BlockingSubscription(slowEntered, releaseSlow);
        var fast = new SignalingSubscription(fastHandled);
        await using var provider = BuildStreamProvider(
            Registration("slow", slow),
            Registration("fast", fast));
        await SeedStreamEventAsync(provider);
        var daemon = CreateSubscriptionDaemon(provider, maxConcurrentWorkers: 2);
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(slowEntered.Task);
            await WaitForTestAsync(fastHandled.Task);
            await WaitForSubscriptionCheckpointAsync(provider, "fast", 1);
        }
        finally
        {
            stop.Cancel();
            releaseSlow.TrySetResult();
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Slow_tenant_checkpoint_does_not_block_another_checkpoint_scope()
    {
        var slowTenant = Guid.Empty;
        var fastTenant = Guid.NewGuid();
        var slowEntered = NewSignal();
        var releaseSlow = NewSignal();
        var fastHandled = NewSignal();
        var subscription = new TenantBlockingSubscription(
            slowTenant,
            slowEntered,
            releaseSlow,
            fastHandled);
        await using var provider = BuildStreamProvider(
            Registration("tenant-worker", subscription));
        await SeedTenantStreamEventsAsync(provider, slowTenant, fastTenant);
        var daemon = CreateSubscriptionDaemon(
            provider,
            maxConcurrentWorkers: 2,
            checkpointScope: CheckpointScope.Tenant);
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(slowEntered.Task);
            await WaitForTestAsync(fastHandled.Task);
            await WaitForSubscriptionCheckpointAsync(
                provider,
                "tenant-worker",
                2,
                fastTenant);
        }
        finally
        {
            stop.Cancel();
            releaseSlow.TrySetResult();
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Tenant_checkpoint_worker_is_discovered_after_daemon_start()
    {
        var tenantId = Guid.NewGuid();
        var handled = NewSignal();
        await using var provider = BuildStreamProvider(
            Registration("dynamic-tenant", new SignalingSubscription(handled)));
        await EnsureStreamDatabaseAsync(provider);
        var daemon = CreateSubscriptionDaemon(
            provider,
            maxConcurrentWorkers: 1,
            checkpointScope: CheckpointScope.Tenant,
            pollingInterval: TimeSpan.FromMilliseconds(10));
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        try
        {
            await SeedTenantStreamEventAsync(provider, tenantId);
            await WaitForTestAsync(handled.Task);
            await WaitForSubscriptionCheckpointAsync(
                provider,
                "dynamic-tenant",
                1,
                tenantId);
        }
        finally
        {
            stop.Cancel();
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Cancellation_stops_a_blocked_worker_without_external_release()
    {
        var entered = NewSignal();
        await using var provider = BuildStreamProvider(
            Registration("cancelable", new CancellationBlockingSubscription(entered)));
        await SeedStreamEventAsync(provider);
        var daemon = CreateSubscriptionDaemon(provider, maxConcurrentWorkers: 1);
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        await WaitForTestAsync(entered.Task);
        stop.Cancel();

        await WaitForTestAsync(execution);
    }

    [Theory]
    [InlineData(BlockingCheckpoint.Idle)]
    [InlineData(BlockingCheckpoint.Paused)]
    [InlineData(BlockingCheckpoint.Failing)]
    public async Task Idle_paused_or_failing_stream_subscription_does_not_block_another_registration(
        BlockingCheckpoint firstCheckpoint)
    {
        var firstLockAttempted = NewSignal();
        var fastHandled = NewSignal();
        ISubscription first = firstCheckpoint == BlockingCheckpoint.Failing
            ? new ThrowingSubscription()
            : new SignalingSubscription(NewSignal());
        await using var provider = BuildStreamProvider(
            Registration("first", first),
            Registration("fast", new SignalingSubscription(fastHandled)));
        await SeedStreamEventAsync(provider);
        await SeedSubscriptionCheckpointAsync(provider, firstCheckpoint);
        var daemon = CreateSubscriptionDaemon(
            provider,
            maxConcurrentWorkers: 2,
            new SignalingLockProvider("first", firstLockAttempted));
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(firstLockAttempted.Task);
            await WaitForTestAsync(fastHandled.Task);
            await WaitForSubscriptionCheckpointAsync(provider, "fast", 1);
        }
        finally
        {
            stop.Cancel();
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Stream_worker_concurrency_is_bounded_and_queued_work_progresses()
    {
        var twoEntered = NewSignal();
        var allEntered = NewSignal();
        using var releases = new SemaphoreSlim(0);
        var tracker = new ConcurrencyTracker(twoEntered, allEntered, releases);
        await using var provider = BuildStreamProvider(
            Registration("one", new TrackingSubscription(tracker)),
            Registration("two", new TrackingSubscription(tracker)),
            Registration("three", new TrackingSubscription(tracker)));
        await SeedStreamEventAsync(provider);
        var daemon = CreateSubscriptionDaemon(provider, maxConcurrentWorkers: 2);
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(twoEntered.Task);
            Assert.Equal(2, tracker.MaxActive);
            Assert.False(allEntered.Task.IsCompleted);

            releases.Release();
            await WaitForTestAsync(allEntered.Task);
            Assert.Equal(2, tracker.MaxActive);
        }
        finally
        {
            stop.Cancel();
            releases.Release(3);
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Contended_lock_wait_does_not_consume_batch_capacity()
    {
        var contendedAttempted = NewSignal();
        var fastHandled = NewSignal();
        await using var provider = BuildStreamProvider(
            Registration("contended", new SignalingSubscription(NewSignal())),
            Registration("fast", new SignalingSubscription(fastHandled)));
        await SeedStreamEventAsync(provider);
        var daemon = CreateSubscriptionDaemon(
            provider,
            maxConcurrentWorkers: 1,
            new BlockingLockProvider("contended", contendedAttempted));
        using var stop = new CancellationTokenSource();
        var execution = RunSubscriptionDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(contendedAttempted.Task);
            await WaitForTestAsync(fastHandled.Task);
            await WaitForSubscriptionCheckpointAsync(provider, "fast", 1);
        }
        finally
        {
            stop.Cancel();
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Slow_outbox_subscription_does_not_block_another_registration()
    {
        var state = new OutboxIsolationState();
        await using var provider = BuildOutboxProvider(state);
        await SeedOutboxMessageAsync(provider);
        var daemon = provider.GetRequiredService<EntityOutboxDaemon<OutboxIsolationDbContext>>();
        using var stop = new CancellationTokenSource();
        var execution = RunOutboxDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(state.SlowEntered.Task);
            await WaitForTestAsync(state.FastHandled.Task);
            await WaitForOutboxCheckpointAsync(provider, "fast", 1);
        }
        finally
        {
            stop.Cancel();
            state.ReleaseSlow.TrySetResult();
            await WaitForTestAsync(execution);
        }
    }

    [Fact]
    public async Task Failing_projection_does_not_block_another_registration()
    {
        var failingEntered = NewSignal();
        var healthyHandled = NewSignal();
        await using var provider = BuildProjectionProvider(
            ProjectionRegistration(
                "failing",
                typeof(FailingProjectionSnapshot),
                (_, _, _, _, _) =>
                {
                    failingEntered.TrySetResult();
                    throw new InvalidOperationException("Projection failed.");
                }),
            ProjectionRegistration(
                "healthy",
                typeof(HealthyProjectionSnapshot),
                (_, _, snapshot, _, _) =>
                {
                    ((HealthyProjectionSnapshot)snapshot).Handled = true;
                    healthyHandled.TrySetResult();
                    return Task.CompletedTask;
                }));
        await SeedProjectionEventAsync(provider);
        var daemon = new ProjectionDaemon<ProjectionIsolationDbContext>(
            NullLogger<ProjectionDaemon<ProjectionIsolationDbContext>>.Instance,
            provider,
            new SignalingLockProvider(),
            Options.Create(new ProjectionDaemonOptions
            {
                MaxConcurrentWorkers = 2,
                PollingInterval = TimeSpan.FromHours(1),
                RetryDelay = TimeSpan.FromHours(1)
            }));
        using var stop = new CancellationTokenSource();
        var execution = RunProjectionDaemonAsync(daemon, stop.Token);

        try
        {
            await WaitForTestAsync(failingEntered.Task);
            await WaitForTestAsync(healthyHandled.Task);
            await WaitForProjectionCheckpointAsync(provider, "healthy", 1);
        }
        finally
        {
            stop.Cancel();
            await WaitForTestAsync(execution);
        }
    }

    private static ServiceProvider BuildStreamProvider(
        params SubscriptionRegistration[] registrations)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<StreamIsolationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        foreach (var registration in registrations)
        {
            services.AddSingleton(registration);
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildOutboxProvider(OutboxIsolationState state)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddDbContext<OutboxIsolationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddEntityOutbox<OutboxIsolationDbContext>(_ => { });
        services.AddOutboxSubscription<SlowOutboxSubscription>(
            options => options.Name = "slow");
        services.AddOutboxSubscription<FastOutboxSubscription>(
            options => options.Name = "fast");
        services.AddEntityOutboxDaemon<OutboxIsolationDbContext>(
            _ => new SignalingLockProvider(),
            options =>
            {
                options.MaxConcurrentWorkers = 2;
                options.PollingInterval = TimeSpan.FromHours(1);
                options.RetryDelay = TimeSpan.FromHours(1);
            });
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProjectionProvider(
        params ProjectionRegistration[] registrations)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<ProjectionIsolationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        foreach (var registration in registrations)
        {
            services.AddSingleton(registration);
        }

        return services.BuildServiceProvider();
    }

    private static SubscriptionDaemon<StreamIsolationDbContext> CreateSubscriptionDaemon(
        IServiceProvider provider,
        int maxConcurrentWorkers,
        IDistributedLockProvider? lockProvider = null,
        CheckpointScope checkpointScope = CheckpointScope.Global,
        TimeSpan? pollingInterval = null) =>
        new(
            NullLogger<SubscriptionDaemon<StreamIsolationDbContext>>.Instance,
            provider,
            lockProvider ?? new SignalingLockProvider(),
            Options.Create(new SubscriptionOptions
            {
                MaxConcurrentWorkers = maxConcurrentWorkers,
                CheckpointScope = checkpointScope,
                PollingInterval = pollingInterval ?? TimeSpan.FromHours(1),
                RetryDelay = TimeSpan.FromHours(1)
            }));

    private static SubscriptionRegistration Registration(
        string name,
        ISubscription subscription) =>
        new()
        {
            Name = name,
            Subscription = subscription,
            Options = new SubscriptionRegistrationOptions()
        };

    private static ProjectionRegistration ProjectionRegistration(
        string name,
        Type snapshotType,
        Func<DbContext, IServiceProvider, object, IEvent, CancellationToken, Task> evolve)
    {
        var options = new ProjectionOptions();
        options.Handles<IsolationEvent>();
        return new ProjectionRegistration
        {
            Name = name,
            Mode = ProjectionMode.Eventual,
            Version = 1,
            ProjectionType = snapshotType,
            SnapshotType = snapshotType,
            Options = options,
            ClearAction = (_, _, _) => Task.CompletedTask,
            EvolveAction = evolve,
            GetOrCreateSnapshotAction = (_, _, _) =>
                Task.FromResult(Activator.CreateInstance(snapshotType)!),
            AddSnapshotAction = (db, snapshot) => db.Add(snapshot)
        };
    }

    private static async Task SeedStreamEventAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StreamIsolationDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Set<DbEvent>().Add(CreateEvent());
        await db.SaveChangesAsync();
    }

    private static async Task EnsureStreamDatabaseAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StreamIsolationDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static async Task SeedTenantStreamEventAsync(
        IServiceProvider provider,
        Guid tenantId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StreamIsolationDbContext>();
        var @event = CreateEvent();
        @event.TenantId = tenantId;
        db.Set<DbEvent>().Add(@event);
        await db.SaveChangesAsync();
    }

    private static async Task SeedProjectionEventAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectionIsolationDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Set<DbEvent>().Add(CreateEvent());
        await db.SaveChangesAsync();
    }

    private static async Task SeedTenantStreamEventsAsync(
        IServiceProvider provider,
        Guid slowTenant,
        Guid fastTenant)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StreamIsolationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var slowEvent = CreateEvent();
        slowEvent.TenantId = slowTenant;
        var fastEvent = CreateEvent();
        fastEvent.Sequence = 2;
        fastEvent.TenantId = fastTenant;
        db.Set<DbEvent>().AddRange(slowEvent, fastEvent);
        await db.SaveChangesAsync();
    }

    private static async Task SeedSubscriptionCheckpointAsync(
        IServiceProvider provider,
        BlockingCheckpoint checkpoint)
    {
        if (checkpoint == BlockingCheckpoint.Failing)
        {
            return;
        }

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StreamIsolationDbContext>();
        db.Set<DbSubscription>().Add(new DbSubscription
        {
            SubscriptionAssemblyQualifiedName = "first",
            Sequence = checkpoint == BlockingCheckpoint.Idle ? 1 : 0,
            State = checkpoint == BlockingCheckpoint.Paused
                ? SubscriptionState.Paused
                : SubscriptionState.Active
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedOutboxMessageAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxIsolationDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Set<DbOutboxMessage>().Add(new DbOutboxMessage
        {
            EventId = Guid.NewGuid(),
            Sequence = 1,
            TypeName = "isolation_event",
            Type = typeof(OutboxPayload).AssemblyQualifiedName!,
            Data = """{"Value":"one"}""",
            Timestamp = DateTimeOffset.UtcNow,
            SourceEntityType = typeof(object).AssemblyQualifiedName!,
            SourceEntityKey = "{}",
            ChangeKind = EntityChangeKind.Added
        });
        await db.SaveChangesAsync();
    }

    private static DbEvent CreateEvent() =>
        new()
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            Sequence = 1,
            Version = 1,
            TypeName = "isolation_event",
            Type = typeof(IsolationEvent).AssemblyQualifiedName!,
            Data = """{"Value":"one"}""",
            Timestamp = DateTimeOffset.UtcNow
        };

    private static Task RunSubscriptionDaemonAsync(
        SubscriptionDaemon<StreamIsolationDbContext> daemon,
        CancellationToken ct) =>
        InvokeExecuteAsync(daemon, ct);

    private static Task RunOutboxDaemonAsync(
        EntityOutboxDaemon<OutboxIsolationDbContext> daemon,
        CancellationToken ct) =>
        InvokeExecuteAsync(daemon, ct);

    private static Task RunProjectionDaemonAsync(
        ProjectionDaemon<ProjectionIsolationDbContext> daemon,
        CancellationToken ct) =>
        InvokeExecuteAsync(daemon, ct);

    private static Task InvokeExecuteAsync<TDaemon>(TDaemon daemon, CancellationToken ct)
    {
        var method = typeof(TDaemon).GetMethod(
            "ExecuteAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task)method!.Invoke(daemon, [ct])!;
    }

    private static Task WaitForTestAsync(Task task) =>
        task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

    private static async Task WaitForSubscriptionCheckpointAsync(
        IServiceProvider provider,
        string name,
        long sequence,
        Guid tenantId = default)
    {
        await WaitForConditionAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StreamIsolationDbContext>();
            return await db.Set<DbSubscription>()
                .AsNoTracking()
                .AnyAsync(
                    row =>
                        row.SubscriptionAssemblyQualifiedName == name &&
                        row.TenantId == tenantId &&
                        row.Sequence >= sequence,
                    TestContext.Current.CancellationToken);
        });
    }

    private static async Task WaitForOutboxCheckpointAsync(
        IServiceProvider provider,
        string name,
        long sequence)
    {
        await WaitForConditionAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxIsolationDbContext>();
            return await db.Set<DbOutboxSubscription>()
                .AsNoTracking()
                .AnyAsync(
                    row =>
                        row.SubscriptionAssemblyQualifiedName == name &&
                        row.Sequence >= sequence,
                    TestContext.Current.CancellationToken);
        });
    }

    private static async Task WaitForProjectionCheckpointAsync(
        IServiceProvider provider,
        string name,
        long sequence)
    {
        await WaitForConditionAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectionIsolationDbContext>();
            return await db.Set<DbProjectionStatus>()
                .AsNoTracking()
                .AnyAsync(
                    row =>
                        row.ProjectionName == name &&
                        row.Position >= sequence,
                    TestContext.Current.CancellationToken);
        });
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!await predicate())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "Timed out waiting for durable daemon progress.");
            await Task.Delay(
                TimeSpan.FromMilliseconds(10),
                TestContext.Current.CancellationToken);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public enum BlockingCheckpoint
    {
        Idle,
        Paused,
        Failing
    }

    private sealed record IsolationEvent(string Value);

    private sealed record OutboxPayload(string Value);

    private sealed class StreamIsolationDbContext(
        DbContextOptions<StreamIsolationDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.UseEventStore();
    }

    private sealed class ProjectionIsolationDbContext(
        DbContextOptions<ProjectionIsolationDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
            modelBuilder.Entity<FailingProjectionSnapshot>().HasKey(x => x.Id);
            modelBuilder.Entity<HealthyProjectionSnapshot>().HasKey(x => x.Id);
        }
    }

    private sealed class OutboxIsolationDbContext(
        DbContextOptions<OutboxIsolationDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);
    }

    private sealed class FailingProjectionSnapshot
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private sealed class HealthyProjectionSnapshot
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public bool Handled { get; set; }
    }

    private sealed class BlockingSubscription(
        TaskCompletionSource entered,
        TaskCompletionSource release) : ISubscription
    {
        public async Task Handle(IEvent @event, CancellationToken ct)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }
    }

    private sealed class CancellationBlockingSubscription(TaskCompletionSource entered)
        : ISubscription
    {
        public async Task Handle(IEvent @event, CancellationToken ct)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class TenantBlockingSubscription(
        Guid slowTenant,
        TaskCompletionSource slowEntered,
        TaskCompletionSource releaseSlow,
        TaskCompletionSource fastHandled) : ISubscription
    {
        public async Task Handle(IEvent @event, CancellationToken ct)
        {
            if (@event.TenantId == slowTenant)
            {
                slowEntered.TrySetResult();
                await releaseSlow.Task.WaitAsync(ct);
                return;
            }

            fastHandled.TrySetResult();
        }
    }

    private sealed class SignalingSubscription(TaskCompletionSource handled) : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct)
        {
            handled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscription : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) =>
            throw new InvalidOperationException("Subscription failed.");
    }

    private sealed class TrackingSubscription(ConcurrencyTracker tracker) : ISubscription
    {
        public async Task Handle(IEvent @event, CancellationToken ct)
        {
            tracker.Enter();
            try
            {
                await tracker.WaitForReleaseAsync(ct);
            }
            finally
            {
                tracker.Exit();
            }
        }
    }

    private sealed class ConcurrencyTracker(
        TaskCompletionSource twoEntered,
        TaskCompletionSource allEntered,
        SemaphoreSlim releases)
    {
        private int _active;
        private int _entered;
        private int _maxActive;

        internal int MaxActive => Volatile.Read(ref _maxActive);

        internal void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            var currentMax = Volatile.Read(ref _maxActive);
            while (active > currentMax)
            {
                var observed = Interlocked.CompareExchange(
                    ref _maxActive,
                    active,
                    currentMax);
                if (observed == currentMax)
                {
                    break;
                }
                currentMax = observed;
            }

            var entered = Interlocked.Increment(ref _entered);
            if (entered >= 2)
            {
                twoEntered.TrySetResult();
            }
            if (entered >= 3)
            {
                allEntered.TrySetResult();
            }
        }

        internal void Exit() => Interlocked.Decrement(ref _active);

        internal Task WaitForReleaseAsync(CancellationToken ct) =>
            releases.WaitAsync(ct);
    }

    private sealed class OutboxIsolationState
    {
        internal TaskCompletionSource SlowEntered { get; } = NewSignal();
        internal TaskCompletionSource ReleaseSlow { get; } = NewSignal();
        internal TaskCompletionSource FastHandled { get; } = NewSignal();
    }

    private sealed class SlowOutboxSubscription(OutboxIsolationState state)
        : IOutboxSubscription
    {
        public async Task Handle(IOutboxEvent @event, CancellationToken ct)
        {
            state.SlowEntered.TrySetResult();
            await state.ReleaseSlow.Task.WaitAsync(ct);
        }
    }

    private sealed class FastOutboxSubscription(OutboxIsolationState state)
        : IOutboxSubscription
    {
        public Task Handle(IOutboxEvent @event, CancellationToken ct)
        {
            state.FastHandled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class SignalingLockProvider(
        string? signaledLockName = null,
        TaskCompletionSource? attempted = null) : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name)
        {
            if (string.Equals(name, signaledLockName, StringComparison.Ordinal))
            {
                attempted?.TrySetResult();
            }
            return new SignalingLock(name);
        }
    }

    private sealed class BlockingLockProvider(
        string blockedName,
        TaskCompletionSource attempted) : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) =>
            string.Equals(name, blockedName, StringComparison.Ordinal)
                ? new BlockingLock(name, attempted)
                : new SignalingLock(name);
    }

    private sealed class BlockingLock(
        string name,
        TaskCompletionSource attempted) : IDistributedLock
    {
        public string Name { get; } = name;

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            attempted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking lock wait completed unexpectedly.");
        }

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SignalingLock(string name) : IDistributedLock
    {
        public string Name { get; } = name;

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new LockHandle();

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new LockHandle());

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            new LockHandle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new LockHandle());
    }

    private sealed class LockHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose()
        {
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
