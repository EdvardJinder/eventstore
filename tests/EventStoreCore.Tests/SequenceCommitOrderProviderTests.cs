using System.Data.Common;
using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PostgresExtensions = EventStoreCore.Postgres.ModelBuilderExtensions;
using SqlServerExtensions = EventStoreCore.SqlServer.ModelBuilderExtensions;

namespace EventStoreCore.Tests;

public sealed class SequenceCommitOrderProviderTests :
    IClassFixture<PostgresFixture>,
    IClassFixture<SqlServerFixture>
{
    private readonly PostgresFixture _postgresFixture;
    private readonly SqlServerFixture _sqlServerFixture;

    public SequenceCommitOrderProviderTests(
        PostgresFixture postgresFixture,
        SqlServerFixture sqlServerFixture)
    {
        _postgresFixture = postgresFixture;
        _sqlServerFixture = sqlServerFixture;
    }

    public static IEnumerable<object[]> Providers =>
    [
        [ProviderKind.Postgres],
        [ProviderKind.SqlServer]
    ];

    [Theory]
    [MemberData(nameof(Providers))]
    public Task Event_sequences_cannot_commit_past_an_in_flight_lower_sequence(
        ProviderKind provider)
    {
        return provider switch
        {
            ProviderKind.Postgres => VerifyEventSequenceCommitOrderAsync<PostgresEventContext>(
                options => options.UseNpgsql(_postgresFixture.ConnectionString)),
            ProviderKind.SqlServer => VerifyEventSequenceCommitOrderAsync<SqlServerEventContext>(
                options => options.UseSqlServer(_sqlServerFixture.ConnectionString)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public Task Entity_outbox_sequences_cannot_commit_past_an_in_flight_lower_sequence(
        ProviderKind provider)
    {
        return provider switch
        {
            ProviderKind.Postgres => VerifyOutboxSequenceCommitOrderAsync<PostgresOutboxContext>(
                options => options.UseNpgsql(_postgresFixture.ConnectionString)),
            ProviderKind.SqlServer => VerifyOutboxSequenceCommitOrderAsync<SqlServerOutboxContext>(
                options => options.UseSqlServer(_sqlServerFixture.ConnectionString)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

    [Fact]
    public async Task Filtered_projection_checkpoint_cannot_skip_delayed_lower_sequence()
    {
        var insertObserver = new PendingSequenceInsertObserver();
        var services = new ServiceCollection();
        services.AddSingleton(insertObserver);
        services.AddDbContext<PostgresEventContext>((sp, options) =>
        {
            options.UseNpgsql(_postgresFixture.ConnectionString);
            options.AddInterceptors(sp.GetRequiredService<PendingSequenceInsertObserver>());
        });
        services.AddEventStore(builder => builder.ExistingDbContext<PostgresEventContext>());

        await using var serviceProvider = services.BuildServiceProvider();
        await using var firstScope = serviceProvider.CreateAsyncScope();
        await using var secondScope = serviceProvider.CreateAsyncScope();
        await using var projectionScope = serviceProvider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<PostgresEventContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<PostgresEventContext>();
        var projectionContext =
            projectionScope.ServiceProvider.GetRequiredService<PostgresEventContext>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await first.Database.EnsureDeletedAsync(cancellationToken);
        await first.Database.EnsureCreatedAsync(cancellationToken);

        const string projectionName = "gap-safe-filtered-sequence";
        first.Set<DbProjectionStatus>().Add(
            new DbProjectionStatus
            {
                ProjectionName = projectionName,
                Version = 1,
                State = ProjectionState.Active,
                Position = 0
            });
        await first.SaveChangesAsync(cancellationToken);

        await using var firstTransaction =
            await first.Database.BeginTransactionAsync(cancellationToken);
        first.Streams.StartStream(
            "orders",
            Guid.NewGuid(),
            events: [new SequenceEvent("matching-lower")]);
        await first.SaveChangesAsync(cancellationToken);
        var firstEvent = first.ChangeTracker.Entries<DbEvent>()
            .Single()
            .Entity;

        var projectionOptions = new ProjectionOptions();
        projectionOptions.Handles<SequenceEvent>();
        projectionOptions.IncludeLogicalEventType(firstEvent.TypeName);
        var evolvedSequences = new List<long>();
        var registration = BuildFilteredProjectionRegistration(
            projectionName,
            projectionOptions,
            evolvedSequences);
        var status = await projectionContext.Set<DbProjectionStatus>()
            .SingleAsync(
                row => row.ProjectionName == projectionName,
                cancellationToken);

        var insertStarted = insertObserver.Arm(typeof(DbEvent));
        second.Streams.StartStream(
            "orders",
            Guid.NewGuid(),
            events: [new IgnoredSequenceEvent("non-matching-higher")]);
        var secondSave = second.SaveChangesAsync(cancellationToken);
        await insertStarted.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        var completed = await Task.WhenAny(
            secondSave,
            Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken));
        if (completed == secondSave)
        {
            await secondSave;
            await ProcessFilteredProjectionBatchAsync(
                projectionContext,
                projectionScope.ServiceProvider,
                registration,
                status,
                cancellationToken);
        }

        await firstTransaction.CommitAsync(cancellationToken);
        await secondSave.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        var secondSequence = second.ChangeTracker.Entries<DbEvent>()
            .Single()
            .Entity
            .Sequence;

        await ProcessFilteredProjectionBatchAsync(
            projectionContext,
            projectionScope.ServiceProvider,
            registration,
            status,
            cancellationToken);

        Assert.Equal([firstEvent.Sequence], evolvedSequences);
        Assert.Equal(secondSequence, status.Position);

        await projectionContext.Database.EnsureDeletedAsync(cancellationToken);
    }

    private static async Task VerifyEventSequenceCommitOrderAsync<TDbContext>(
        Action<DbContextOptionsBuilder> configureProvider)
        where TDbContext : DbContext
    {
        var insertObserver = new PendingSequenceInsertObserver();
        var services = new ServiceCollection();
        services.AddSingleton(insertObserver);
        services.AddDbContext<TDbContext>((sp, options) =>
        {
            configureProvider(options);
            options.AddInterceptors(sp.GetRequiredService<PendingSequenceInsertObserver>());
        });
        services.AddEventStore(builder => builder.ExistingDbContext<TDbContext>());

        await using var serviceProvider = services.BuildServiceProvider();
        await using var firstScope = serviceProvider.CreateAsyncScope();
        await using var secondScope = serviceProvider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<TDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<TDbContext>();
        await first.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await first.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        await using var firstTransaction =
            await first.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        first.Streams.StartStream(Guid.NewGuid(), new SequenceEvent("first"));
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        var firstSequence = first.ChangeTracker.Entries<DbEvent>()
            .Single()
            .Entity
            .Sequence;

        var insertStarted = insertObserver.Arm(typeof(DbEvent));
        second.Streams.StartStream(Guid.NewGuid(), new SequenceEvent("second"));
        var secondSave = second.SaveChangesAsync(TestContext.Current.CancellationToken);
        await insertStarted.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        Assert.NotSame(
            secondSave,
            await Task.WhenAny(
                secondSave,
                Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    TestContext.Current.CancellationToken)));

        await firstTransaction.CommitAsync(TestContext.Current.CancellationToken);
        await secondSave.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        var secondSequence = second.ChangeTracker.Entries<DbEvent>()
            .Single()
            .Entity
            .Sequence;

        Assert.True(secondSequence > firstSequence);
        var page = await second.EventLog.ReadPageAsync(
            new EventLogReadOptions
            {
                AfterSequence = firstSequence,
                MaxCount = 10
            },
            TestContext.Current.CancellationToken);
        Assert.Equal([secondSequence], page.Events.Select(@event => @event.Sequence));

        await second.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    private static async Task VerifyOutboxSequenceCommitOrderAsync<TDbContext>(
        Action<DbContextOptionsBuilder> configureProvider)
        where TDbContext : DbContext
    {
        var insertObserver = new PendingSequenceInsertObserver();
        var services = new ServiceCollection();
        services.AddSingleton(insertObserver);
        services.AddDbContext<TDbContext>((sp, options) =>
        {
            configureProvider(options);
            options.AddInterceptors(sp.GetRequiredService<PendingSequenceInsertObserver>());
        });
        services.AddEntityOutbox<TDbContext>(outbox =>
        {
            outbox.AddEvent<EntityCreated>();
            outbox.For<TrackedEntity>()
                .On(change => change.Added(
                    entry => new EntityCreated(entry.Entity.Id)));
        });

        await using var serviceProvider = services.BuildServiceProvider();
        await using var firstScope = serviceProvider.CreateAsyncScope();
        await using var secondScope = serviceProvider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<TDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<TDbContext>();
        await first.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await first.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        await using var firstTransaction =
            await first.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        first.Add(new TrackedEntity { Id = Guid.NewGuid() });
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        var firstSequence = first.ChangeTracker.Entries<DbOutboxMessage>()
            .Single()
            .Entity
            .Sequence;

        var insertStarted = insertObserver.Arm(typeof(DbOutboxMessage));
        second.Add(new TrackedEntity { Id = Guid.NewGuid() });
        var secondSave = second.SaveChangesAsync(TestContext.Current.CancellationToken);
        await insertStarted.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        Assert.NotSame(
            secondSave,
            await Task.WhenAny(
                secondSave,
                Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    TestContext.Current.CancellationToken)));

        await firstTransaction.CommitAsync(TestContext.Current.CancellationToken);
        await secondSave.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        var secondSequence = second.ChangeTracker.Entries<DbOutboxMessage>()
            .Single()
            .Entity
            .Sequence;

        Assert.True(secondSequence > firstSequence);
        var reader = secondScope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var messages = await reader.ReadAsync(
            firstSequence,
            maxCount: 10,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal([secondSequence], messages.Select(message => message.Sequence));

        await second.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    private static ProjectionRegistration BuildFilteredProjectionRegistration(
        string name,
        ProjectionOptions options,
        ICollection<long> evolvedSequences) =>
        new()
        {
            Name = name,
            Mode = ProjectionMode.Eventual,
            Version = 1,
            ProjectionType = typeof(SequenceProjectionSnapshot),
            SnapshotType = typeof(SequenceProjectionSnapshot),
            Options = options,
            ClearAction = (_, _, _) => Task.CompletedTask,
            EvolveAction = (_, _, snapshot, @event, _) =>
            {
                var projection = (SequenceProjectionSnapshot)snapshot;
                projection.Id = @event.StreamId;
                evolvedSequences.Add(@event.Sequence);
                return Task.CompletedTask;
            },
            GetOrCreateSnapshotAction = async (db, key, ct) =>
            {
                var streamId = (Guid)key;
                return await db.Set<SequenceProjectionSnapshot>()
                    .FindAsync([streamId], ct)
                    ?? new SequenceProjectionSnapshot { Id = streamId };
            },
            AddSnapshotAction = (db, snapshot) => db.Add(snapshot)
        };

    private static async Task<bool> ProcessFilteredProjectionBatchAsync<TDbContext>(
        TDbContext dbContext,
        IServiceProvider services,
        ProjectionRegistration registration,
        DbProjectionStatus status,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        var daemon = new ProjectionDaemon<TDbContext>(
            NullLogger<ProjectionDaemon<TDbContext>>.Instance,
            services,
            Substitute.For<IDistributedLockProvider>(),
            Options.Create(new ProjectionDaemonOptions { BatchSize = 10 }));
        var processBatch = typeof(ProjectionDaemon<TDbContext>).GetMethod(
            "ProcessBatchAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!;
        var task = (Task<bool>)processBatch.Invoke(
            daemon,
            [dbContext, services, registration, status, cancellationToken])!;
        return await task;
    }

    public enum ProviderKind
    {
        Postgres,
        SqlServer
    }

    private sealed record SequenceEvent(string Name);

    private sealed record IgnoredSequenceEvent(string Name);

    private sealed class SequenceProjectionSnapshot
    {
        public Guid Id { get; set; }
    }

    private sealed record EntityCreated(Guid EntityId);

    private sealed class TrackedEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class PostgresEventContext(
        DbContextOptions<PostgresEventContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            PostgresExtensions.UseEventStore(modelBuilder);
            modelBuilder.Entity<SequenceProjectionSnapshot>().HasKey(snapshot => snapshot.Id);
        }
    }

    private sealed class SqlServerEventContext(
        DbContextOptions<SqlServerEventContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqlServerExtensions.UseEventStore(modelBuilder);
        }
    }

    private sealed class PostgresOutboxContext(
        DbContextOptions<PostgresOutboxContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrackedEntity>();
            PostgresExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    private sealed class SqlServerOutboxContext(
        DbContextOptions<SqlServerOutboxContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrackedEntity>();
            SqlServerExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    private sealed class PendingSequenceInsertObserver : DbCommandInterceptor
    {
        private TaskCompletionSource? _insertStarted;
        private Type? _entityType;

        internal Task Arm(Type entityType)
        {
            _entityType = entityType;
            _insertStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _insertStarted.Task;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Signal(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Signal(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Signal(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Signal(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private void Signal(DbContext? dbContext)
        {
            if (_insertStarted is null || _entityType is null || dbContext is null)
            {
                return;
            }

            if (dbContext.ChangeTracker.Entries()
                .Any(entry =>
                    entry.State == EntityState.Added &&
                    entry.Entity.GetType() == _entityType))
            {
                _insertStarted.TrySetResult();
            }
        }
    }
}
