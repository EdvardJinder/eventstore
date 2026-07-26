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
