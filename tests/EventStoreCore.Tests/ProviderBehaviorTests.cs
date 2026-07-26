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
    public async Task IdempotentAppend_ExactRetryRecoversCommittedResult(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        var operationKey = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var operation = new AppendOperation(
            streamId,
            ExpectedVersion.NoStream,
            [new SampleEvent { Name = "first" }.WithEventId(eventId)])
        {
            StreamType = "orders",
            IdempotencyKey = operationKey
        };

        var first = await context.Streams.AppendAsync(operation, TestContext.Current.CancellationToken);
        await context.Streams.AppendAsync(
            "orders",
            streamId,
            ExpectedVersion.Exact(1),
            [new SampleEvent { Name = "later" }],
            TestContext.Current.CancellationToken);
        var retry = await context.Streams.AppendAsync(operation, TestContext.Current.CancellationToken);

        Assert.False(first.WasAlreadyCommitted);
        Assert.True(retry.WasAlreadyCommitted);
        Assert.Equal(first.PreviousVersion, retry.PreviousVersion);
        Assert.Equal(first.CurrentVersion, retry.CurrentVersion);
        Assert.Equal(first.Events, retry.Events);
        Assert.Equal(eventId, retry.Events[0].EventId);
        Assert.Equal(1, retry.CurrentVersion);

        var stream = await context.Streams.FetchForReadingAsync(
            "orders",
            streamId,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, stream!.Version);
        Assert.Equal(2, stream.Events.Count);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task IdempotentAppend_ConflictingReuseIsRejected(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        var operationKey = Guid.NewGuid();
        await context.Streams.AppendAsync(
            new AppendOperation(
                streamId,
                ExpectedVersion.NoStream,
                [new SampleEvent { Name = "original" }])
            {
                IdempotencyKey = operationKey
            },
            TestContext.Current.CancellationToken);

        var conflict = await Assert.ThrowsAsync<EventStoreIdempotencyConflictException>(() =>
            context.Streams.AppendAsync(
                new AppendOperation(
                    streamId,
                    ExpectedVersion.NoStream,
                    [new SampleEvent { Name = "different" }])
                {
                    IdempotencyKey = operationKey
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(operationKey, conflict.IdempotencyKey);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task CallerEventId_ExactRetrySucceedsAndConflictingReuseIsRejected(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var exact = new AppendOperation(
            streamId,
            ExpectedVersion.NoStream,
            [new SampleEvent { Name = "original" }.WithEventId(eventId)]);

        var first = await context.Streams.AppendAsync(exact, TestContext.Current.CancellationToken);
        var retry = await context.Streams.AppendAsync(exact, TestContext.Current.CancellationToken);
        var conflict = await Assert.ThrowsAsync<EventStoreIdempotencyConflictException>(() =>
            context.Streams.AppendAsync(
                new AppendOperation(
                    streamId,
                    ExpectedVersion.NoStream,
                    [new SampleEvent { Name = "different" }.WithEventId(eventId)]),
                TestContext.Current.CancellationToken));

        Assert.False(first.WasAlreadyCommitted);
        Assert.True(retry.WasAlreadyCommitted);
        Assert.Equal(first.Events, retry.Events);
        Assert.Equal(eventId, conflict.EventId);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task IdempotentAppend_ConcurrentExactRetriesCommitOnce(ProviderKind provider)
    {
        await using (var setup = CreateContext(provider))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var streamId = Guid.NewGuid();
        var operationKey = Guid.NewGuid();
        var operation = new AppendOperation(
            streamId,
            ExpectedVersion.NoStream,
            [new SampleEvent { Name = "once" }])
        {
            IdempotencyKey = operationKey
        };

        async Task<AppendResult> AppendFromNewContext()
        {
            await using var writer = CreateContext(provider);
            return await writer.Streams.AppendAsync(operation, TestContext.Current.CancellationToken);
        }

        var results = await Task.WhenAll(AppendFromNewContext(), AppendFromNewContext());

        Assert.Single(results, result => !result.WasAlreadyCommitted);
        Assert.Single(results, result => result.WasAlreadyCommitted);
        Assert.Equal(results[0].Events, results[1].Events);

        await using var verification = CreateContext(provider);
        var stream = await verification.Streams.FetchForReadingAsync(
            streamId,
            TestContext.Current.CancellationToken);
        Assert.Single(stream!.Events);
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
