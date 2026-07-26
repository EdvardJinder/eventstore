using EventStoreCore.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace EventStoreCore.Tests;

public sealed class StreamPagingAndEvolutionTests
{
    [Fact]
    public async Task ReadPageAsync_HandlesForwardAndBackwardBoundaries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new PagingDbContext(
            new DbContextOptionsBuilder<PagingDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        db.Streams.StartStream(
            streamId,
            events: Enumerable.Range(1, 25).Select(i => new HistoricalEvent(i)));
        AssignPendingSequences(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var forward = await db.Streams.ReadPageAsync(
            streamId,
            new StreamReadOptions { FromVersion = 6, ToVersion = 22, MaxCount = 10 },
            TestContext.Current.CancellationToken);
        Assert.NotNull(forward);
        Assert.Equal(25, forward.StreamVersion);
        Assert.Equal(Enumerable.Range(6, 10), forward.Events.Select(x => (int)x.Version));
        Assert.Equal(16, forward.NextVersion);

        var backward = await db.Streams.ReadPageAsync(
            streamId,
            new StreamReadOptions
            {
                Direction = StreamReadDirection.Backward,
                FromVersion = 22,
                ToVersion = 6,
                MaxCount = 10
            },
            TestContext.Current.CancellationToken);
        Assert.NotNull(backward);
        Assert.Equal(Enumerable.Range(13, 10).Reverse(), backward.Events.Select(x => (int)x.Version));
        Assert.Equal(12, backward.NextVersion);
    }

    [Fact]
    public async Task ReadAsync_EnumeratesAllPagesWithoutLoadingTheWholeStream()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new PagingDbContext(
            new DbContextOptionsBuilder<PagingDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        db.Streams.StartStream(
            streamId,
            events: Enumerable.Range(1, 31).Select(i => new HistoricalEvent(i)));
        AssignPendingSequences(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var versions = new List<long>();
        await foreach (var @event in db.Streams.ReadAsync(
            streamId,
            new StreamReadOptions { FromVersion = 3, ToVersion = 29, MaxCount = 7 },
            TestContext.Current.CancellationToken))
        {
            versions.Add(@event.Version);
        }

        Assert.Equal(Enumerable.Range(3, 27).Select(x => (long)x), versions);
    }

    [Fact]
    public async Task Metadata_IsPersistedAndReadAsAnImmutableEnvelope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new PagingDbContext(
            new DbContextOptionsBuilder<PagingDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var headers = new Dictionary<string, string> { ["trace"] = "abc" };
        var streamId = Guid.NewGuid();
        db.Streams.StartStream(
            "orders",
            streamId,
            Guid.Empty,
            new HistoricalEvent(1).WithMetadata(
                new EventMetadata(correlationId, causationId, "user:42", headers)));
        headers["trace"] = "mutated";
        AssignPendingSequences(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await db.Streams.ReadPageAsync(
            "orders",
            streamId,
            new StreamReadOptions(),
            TestContext.Current.CancellationToken);
        var @event = Assert.Single(page!.Events);

        Assert.Equal(correlationId, @event.Metadata.CorrelationId);
        Assert.Equal(causationId, @event.Metadata.CausationId);
        Assert.Equal("user:42", @event.Metadata.Actor);
        Assert.Equal("abc", @event.Metadata.Headers["trace"]);
        Assert.Equal("orders", @event.Metadata.StreamType);
        Assert.Equal(1, @event.Metadata.StreamVersion);
        Assert.Equal(@event.Sequence, @event.Metadata.GlobalSequence);
    }

    [Fact]
    public void VersionedUpcasters_RunAsOneDeterministicChain()
    {
        var order = new List<int>();
        var services = new ServiceCollection();
        services.AddEventStore(builder => builder.AddEvent<HistoricalEvent>(
            "historical_event",
            schemaVersion: 3,
            type => type
                .AddUpcaster(1, 2, json =>
                {
                    order.Add(1);
                    var node = JsonNode.Parse(json)!.AsObject();
                    node["Value"] = node["LegacyValue"]!.GetValue<int>();
                    node.Remove("LegacyValue");
                    return node.ToJsonString();
                })
                .AddUpcaster(2, 3, json =>
                {
                    order.Add(2);
                    return json;
                })));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<EventTypeRegistry>();
        var serializer = provider.GetRequiredService<IEventStoreSerializer>();
        var stored = new DbEvent
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            Type = "missing",
            TypeName = "historical_event",
            SchemaVersion = 1,
            Data = """{"LegacyValue":42}""",
            Version = 1
        };

        var materialized = stored.ToEvent(registry, serializer);

        Assert.Equal([1, 2], order);
        Assert.Equal(42, Assert.IsType<HistoricalEvent>(materialized.Data).Value);
    }

    private sealed record HistoricalEvent(int Value);

    private static void AssignPendingSequences(DbContext dbContext)
    {
        var sequence = 1L;
        foreach (var entry in dbContext.ChangeTracker.Entries<DbEvent>()
                     .Where(entry => entry.State == EntityState.Added)
                     .OrderBy(entry => entry.Entity.Version))
        {
            entry.Entity.Sequence = sequence++;
        }
    }

    private sealed class PagingDbContext(DbContextOptions<PagingDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);
    }
}
