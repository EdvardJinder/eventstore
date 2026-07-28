using EventStoreCore.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Tests;

public sealed class SqliteProviderTests
{
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

    [Fact]
    public async Task Sqlite_serializes_generated_sequences_until_commit()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"eventstore-sequence-order-{Guid.NewGuid():N}.db");
        var connectionString =
            $"Data Source={databasePath};Default Timeout=1;Pooling=False";
        var options = new DbContextOptionsBuilder<SqliteEventStoreContext>()
            .UseSqlite(connectionString)
            .Options;

        try
        {
            await using var first = new SqliteEventStoreContext(options);
            await using var second = new SqliteEventStoreContext(options);
            await first.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            await using var firstTransaction =
                await first.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            first.Streams.StartStream(Guid.NewGuid(), new SampleEvent("first"));
            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
            var firstSequence = first.ChangeTracker.Entries<DbEvent>()
                .Single()
                .Entity
                .Sequence;

            second.Streams.StartStream(Guid.NewGuid(), new SampleEvent("second"));
            var sqliteException = await Assert.ThrowsAsync<SqliteException>(
                () => second.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Equal(5, sqliteException.SqliteErrorCode);

            await firstTransaction.CommitAsync(TestContext.Current.CancellationToken);
            await second.SaveChangesAsync(TestContext.Current.CancellationToken);
            var secondSequence = second.ChangeTracker.Entries<DbEvent>()
                .Single()
                .Entity
                .Sequence;

            Assert.True(secondSequence > firstSequence);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Sqlite_recovers_exact_caller_event_id_retries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<SqliteEventStoreContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new SqliteEventStoreContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var streamId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var operation = new AppendOperation(
            streamId,
            ExpectedVersion.NoStream,
            [new SampleEvent("original").WithEventId(eventId)]);

        var first = await context.Streams.AppendAsync(
            operation,
            TestContext.Current.CancellationToken);
        var retry = await context.Streams.AppendAsync(
            operation,
            TestContext.Current.CancellationToken);
        var conflict = await Assert.ThrowsAsync<EventStoreIdempotencyConflictException>(
            () => context.Streams.AppendAsync(
                new AppendOperation(
                    streamId,
                    ExpectedVersion.NoStream,
                    [new SampleEvent("changed").WithEventId(eventId)]),
                TestContext.Current.CancellationToken));

        Assert.False(first.WasAlreadyCommitted);
        Assert.True(retry.WasAlreadyCommitted);
        Assert.Equal(first.Events, retry.Events);
        Assert.Equal(eventId, conflict.EventId);
        Assert.Single(
            await context.Set<DbEvent>()
                .ToArrayAsync(TestContext.Current.CancellationToken));
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
