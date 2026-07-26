using EventStoreCore;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using PostgresExtensions = EventStoreCore.Postgres.ModelBuilderExtensions;
using SqlServerExtensions = EventStoreCore.SqlServer.ModelBuilderExtensions;

namespace EventStoreCore.Tests;

public class ProviderBehaviorTests : IClassFixture<PostgresFixture>, IClassFixture<SqlServerFixture>
{
    private readonly PostgresFixture _postgresFixture;
    private readonly SqlServerFixture _sqlServerFixture;

    public ProviderBehaviorTests(
        PostgresFixture postgresFixture,
        SqlServerFixture sqlServerFixture)
    {
        _postgresFixture = postgresFixture;
        _sqlServerFixture = sqlServerFixture;
    }

    public enum ProviderKind
    {
        Postgres,
        SqlServer
    }

    public static IEnumerable<object[]> Providers =>
    [
        [ProviderKind.Postgres],
        [ProviderKind.SqlServer]
    ];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task EventStore_WritesAndReads(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var store = new DbContextEventStore(context);
        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        store.StartStream(streamId, tenantId, events: new SampleEvent { Name = "Hello" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stream = await store.FetchForReadingAsync(streamId, tenantId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        Assert.Single(stream!.Events);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task EventStore_ReadsLargeStreamsAcrossPageBoundaries(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var store = new DbContextEventStore(context);
        var streamId = Guid.NewGuid();
        store.StartStream(
            streamId,
            events: Enumerable.Range(1, 105).Select(i => new SampleEvent { Name = i.ToString() }));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await store.ReadPageAsync(
            streamId,
            new StreamReadOptions { FromVersion = 33, ToVersion = 97, MaxCount = 32 },
            TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(Enumerable.Range(33, 32).Select(x => (long)x), page.Events.Select(x => x.Version));
        Assert.Equal(65, page.NextVersion);
        Assert.Equal(105, page.StreamVersion);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task EventStore_ReadsTheGlobalLogAcrossStreams(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        context.Streams.StartStream(
            "orders",
            Guid.NewGuid(),
            events: new SampleEvent { Name = "first" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Streams.StartStream(
            "customers",
            Guid.NewGuid(),
            events: new SampleEvent { Name = "second" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await context.EventLog.ReadPageAsync(
            new EventLogReadOptions { MaxCount = 10 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Events.Count);
        Assert.True(page.Events[0].Sequence < page.Events[1].Sequence);
        Assert.Equal(["orders", "customers"], page.Events.Select(@event => @event.StreamType));
        Assert.Equal(page.Events[1].Sequence, page.HeadSequence);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task StreamLifecycle_IsAuditedAndProviderSafe(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        context.Streams.StartStream(
            "orders",
            streamId,
            tenantId,
            events: new SampleEvent { Name = "created" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var archived = await context.StreamLifecycle.ArchiveAsync(
            "orders",
            streamId,
            tenantId,
            expectedVersion: 1,
            new StreamLifecycleChange
            {
                Actor = "retention-service",
                Reason = "Order reached the archive threshold.",
                CorrelationId = "governance-123"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StreamLifecycleState.Archived, archived.State);
        Assert.Equal(StreamLifecycleState.Archived, Assert.Single(archived.History).ToState);

        var archivedRead = await context.Streams.FetchForReadingAsync(
            "orders",
            streamId,
            tenantId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(archivedRead);
        Assert.Equal(StreamLifecycleState.Archived, archivedRead!.LifecycleState);

        var archiveWrite = await Assert.ThrowsAsync<StreamNotWritableException>(() =>
            context.Streams.AppendAsync(
                "orders",
                streamId,
                tenantId,
                ExpectedVersion.Exact(1),
                [new SampleEvent { Name = "rejected" }],
                TestContext.Current.CancellationToken));
        Assert.Equal(StreamLifecycleState.Archived, archiveWrite.LifecycleState);

        await using (var applicationTransaction =
            await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await context.StreamLifecycle.RestoreAsync(
                "orders",
                streamId,
                tenantId,
                expectedVersion: 1,
                new StreamLifecycleChange
                {
                    Actor = "operations",
                    Reason = "The order is active again."
                },
                TestContext.Current.CancellationToken);
            await applicationTransaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await context.Streams.AppendAsync(
            "orders",
            streamId,
            tenantId,
            ExpectedVersion.Exact(1),
            [new SampleEvent { Name = "restored" }],
            TestContext.Current.CancellationToken);

        var tombstoned = await context.StreamLifecycle.TombstoneAsync(
            "orders",
            streamId,
            tenantId,
            expectedVersion: 2,
            new StreamLifecycleChange
            {
                Actor = "privacy-administrator",
                Reason = "Administrative tombstone request."
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StreamLifecycleState.Tombstoned, tombstoned.State);
        Assert.Equal(3, tombstoned.History.Count);
        Assert.Equal(
            [
                StreamLifecycleState.Archived,
                StreamLifecycleState.Active,
                StreamLifecycleState.Tombstoned
            ],
            tombstoned.History.Select(x => x.ToState));

        Assert.Null(await context.Streams.FetchForReadingAsync(
            "orders",
            streamId,
            tenantId,
            TestContext.Current.CancellationToken));
        Assert.Null(await context.Streams.ReadPageAsync(
            "orders",
            streamId,
            tenantId,
            new StreamReadOptions(),
            TestContext.Current.CancellationToken));

        var tombstoneWrite = await Assert.ThrowsAsync<StreamNotWritableException>(() =>
            context.Streams.AppendAsync(
                "orders",
                streamId,
                tenantId,
                ExpectedVersion.Any,
                [new SampleEvent { Name = "rejected" }],
                TestContext.Current.CancellationToken));
        Assert.Equal(StreamLifecycleState.Tombstoned, tombstoneWrite.LifecycleState);

        var eventLog = await context.EventLog.ReadPageAsync(
            new EventLogReadOptions
            {
                TenantId = tenantId,
                StreamTypes = ["orders"],
                MaxCount = 10
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(2, eventLog.Events.Count(x => x.StreamId == streamId));

        var audit = await context.StreamLifecycle.GetAsync(
            "orders",
            streamId,
            tenantId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(audit);
        Assert.Equal(StreamLifecycleState.Tombstoned, audit!.State);
        Assert.Equal("privacy-administrator", audit.History[^1].Actor);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task StreamLifecycle_IsTenantScopedAndUsesExactVersions(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        context.Streams.StartStream("orders", streamId, tenantA, events: new SampleEvent());
        context.Streams.StartStream("orders", streamId, tenantB, events: new SampleEvent());
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var conflict = await Assert.ThrowsAsync<StreamLifecycleConflictException>(() =>
            context.StreamLifecycle.ArchiveAsync(
                "orders",
                streamId,
                tenantA,
                expectedVersion: 0,
                new StreamLifecycleChange { Actor = "admin", Reason = "test" },
                TestContext.Current.CancellationToken));
        Assert.Equal(1, conflict.ActualVersion);
        Assert.Equal(StreamLifecycleState.Active, conflict.ActualState);

        await context.StreamLifecycle.TombstoneAsync(
            "orders",
            streamId,
            tenantA,
            expectedVersion: 1,
            new StreamLifecycleChange { Actor = "admin", Reason = "tenant A only" },
            TestContext.Current.CancellationToken);

        Assert.Null(await context.Streams.FetchForReadingAsync(
            "orders",
            streamId,
            tenantA,
            TestContext.Current.CancellationToken));
        Assert.NotNull(await context.Streams.FetchForReadingAsync(
            "orders",
            streamId,
            tenantB,
            TestContext.Current.CancellationToken));

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task StreamLifecycle_AndAppend_CannotBothWinTheSameVersion(ProviderKind provider)
    {
        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await using (var setup = CreateContext(provider))
        {
            await setup.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.Streams.StartStream(
                "orders",
                streamId,
                tenantId,
                events: new SampleEvent { Name = "created" });
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        async Task<Exception?> TryArchiveAsync()
        {
            try
            {
                await using var archiveContext = CreateContext(provider);
                await archiveContext.StreamLifecycle.ArchiveAsync(
                    "orders",
                    streamId,
                    tenantId,
                    expectedVersion: 1,
                    new StreamLifecycleChange
                    {
                        Actor = "governance",
                        Reason = "concurrency test"
                    },
                    TestContext.Current.CancellationToken);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        async Task<Exception?> TryAppendAsync()
        {
            try
            {
                await using var appendContext = CreateContext(provider);
                await appendContext.Streams.AppendAsync(
                    "orders",
                    streamId,
                    tenantId,
                    ExpectedVersion.Exact(1),
                    [new SampleEvent { Name = "concurrent append" }],
                    TestContext.Current.CancellationToken);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(TryArchiveAsync(), TryAppendAsync());
        Assert.Single(results, result => result is null);
        var failure = Assert.Single(results, result => result is not null);
        Assert.True(
            failure is StreamLifecycleConflictException
                or EventStoreConcurrencyException
                or StreamNotWritableException,
            $"Unexpected concurrency failure type: {failure!.GetType().FullName}");

        await using var verification = CreateContext(provider);
        var lifecycle = await verification.StreamLifecycle.GetAsync(
            "orders",
            streamId,
            tenantId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(lifecycle);
        Assert.True(
            (lifecycle!.State == StreamLifecycleState.Archived
                && lifecycle.StreamVersion == 1
                && lifecycle.History.Count == 1)
            || (lifecycle.State == StreamLifecycleState.Active
                && lifecycle.StreamVersion == 2
                && lifecycle.History.Count == 0));

        await verification.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    private DbContext CreateContext(ProviderKind provider)
    {
        return provider switch
        {
            ProviderKind.Postgres => new PostgresContext(new DbContextOptionsBuilder<PostgresContext>()
                .UseNpgsql(_postgresFixture.ConnectionString)
                .Options),
            ProviderKind.SqlServer => new SqlServerContext(new DbContextOptionsBuilder<SqlServerContext>()
                .UseSqlServer(_sqlServerFixture.ConnectionString)
                .Options),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }


    private sealed class PostgresContext : DbContext
    {
        public PostgresContext(DbContextOptions<PostgresContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            PostgresExtensions.UseEventStore(modelBuilder);
        }
    }

    private sealed class SqlServerContext : DbContext
    {
        public SqlServerContext(DbContextOptions<SqlServerContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqlServerExtensions.UseEventStore(modelBuilder);
        }
    }


    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }
}
