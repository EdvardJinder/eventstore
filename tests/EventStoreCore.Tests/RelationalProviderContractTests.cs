using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Tests;

public sealed class RelationalProviderContractTests
{
    [Fact]
    public void Community_provider_can_configure_the_complete_model_without_persistence_types()
    {
        var options = new DbContextOptionsBuilder<CommunityProviderContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new CommunityProviderContext(options);
        var eventType = context.Model.FindEntityType("EventStoreCore.DbEvent");
        var snapshotType = context.Model.FindEntityType("EventStoreCore.DbSnapshot");

        Assert.NotNull(eventType);
        Assert.NotNull(snapshotType);
        Assert.Equal("TEXT", eventType!.FindProperty("Data")?.GetColumnType());
        Assert.Equal("TEXT", eventType.FindProperty("Headers")?.GetColumnType());
        Assert.Equal("TEXT", snapshotType!.FindProperty("Data")?.GetColumnType());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Provider_boundary_rejects_missing_column_types(string? columnType)
    {
        var options = new DbContextOptionsBuilder<InvalidProviderContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new InvalidProviderContext(options, columnType!).Model);

        Assert.Equal("serializedDataColumnType", exception.ParamName);
    }

    [Fact]
    public async Task Sqlite_provider_generates_global_sequences_and_reads_the_log()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<SqliteEventStoreContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new SqliteEventStoreContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        context.Streams.StartStream(
            "orders",
            Guid.NewGuid(),
            events: [new SampleEvent("first"), new SampleEvent("second")]);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Streams.StartStream(
            "customers",
            Guid.NewGuid(),
            events: [new SampleEvent("third")]);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await context.EventLog.ReadPageAsync(
            new() { MaxCount = 10 },
            TestContext.Current.CancellationToken);

        Assert.Equal([1L, 2L, 3L], page.Events.Select(@event => @event.Sequence));
        Assert.Equal(["orders", "orders", "customers"], page.Events.Select(@event => @event.StreamType));
        Assert.Equal(3, page.HeadSequence);

        var threshold = DateTimeOffset.UtcNow.AddMinutes(-1);
        var recentEventCount = await context.Set<DbEvent>()
            .CountAsync(
                @event => @event.Timestamp >= threshold,
                TestContext.Current.CancellationToken);
        Assert.Equal(3, recentEventCount);
    }

    [Fact]
    public async Task Sqlite_provider_generates_entity_outbox_sequences()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<SqliteOutboxContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new SqliteOutboxContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        context.Set<DbOutboxMessage>().AddRange(CreateOutboxMessage(), CreateOutboxMessage());

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sequences = await context.Set<DbOutboxMessage>()
            .OrderBy(message => message.Sequence)
            .Select(message => message.Sequence)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1L, 2L], sequences);

        var threshold = DateTimeOffset.UtcNow.AddMinutes(-1);
        var recentMessageCount = await context.Set<DbOutboxMessage>()
            .CountAsync(
                message => message.Timestamp >= threshold,
                TestContext.Current.CancellationToken);
        Assert.Equal(2, recentMessageCount);
    }

    private static DbOutboxMessage CreateOutboxMessage() =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            TypeName = "sample",
            Data = "{}",
            Timestamp = DateTimeOffset.UtcNow,
            SourceEntityType = typeof(object).AssemblyQualifiedName!,
            SourceEntityKey = "{}"
        };

    private sealed class CommunityProviderContext(DbContextOptions<CommunityProviderContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventStoreRelationalModel(
                new RelationalProviderModelOptions("TEXT"));
        }
    }

    private sealed class InvalidProviderContext(
        DbContextOptions<InvalidProviderContext> options,
        string columnType)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventStoreRelationalModel(
                new RelationalProviderModelOptions(columnType));
        }
    }

    private sealed class SqliteEventStoreContext(DbContextOptions<SqliteEventStoreContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            global::EventStoreCore.Sqlite.ModelBuilderExtensions.UseEventStore(modelBuilder);
        }
    }

    private sealed class SqliteOutboxContext(DbContextOptions<SqliteOutboxContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            global::EventStoreCore.Sqlite.ModelBuilderExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    private sealed record SampleEvent(string Name);
}
