using System.Data.Common;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

    public enum ProviderKind
    {
        Postgres,
        SqlServer
    }

    private sealed record SequenceEvent(string Name);

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
