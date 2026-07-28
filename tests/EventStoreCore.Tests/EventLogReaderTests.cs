using EventStoreCore.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public sealed class EventLogReaderTests
{
    [Fact]
    public async Task ReadPageAsync_pages_across_streams_with_a_stable_global_cursor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await fixture.AppendAsync("orders", Guid.NewGuid(), tenantA, new OrderCreated(1), 1);
        await fixture.AppendAsync("customers", Guid.NewGuid(), tenantB, new CustomerCreated(2), 2);
        await fixture.AppendAsync("orders", Guid.NewGuid(), tenantA, new OrderCreated(3), 3);
        await fixture.AppendAsync("invoices", Guid.NewGuid(), tenantA, new InvoiceIssued(4), 4);
        await fixture.AppendAsync("orders", Guid.NewGuid(), tenantB, new OrderCreated(5), 5);

        var first = await fixture.Db.EventLog.ReadPageAsync(
            new EventLogReadOptions { MaxCount = 2 },
            TestContext.Current.CancellationToken);
        Assert.Equal([1L, 2L], first.Events.Select(@event => @event.Sequence));
        Assert.Equal(5, first.HeadSequence);
        Assert.Equal(2, first.NextSequence);

        var second = await fixture.Db.EventLog.ReadPageAsync(
            new EventLogReadOptions
            {
                AfterSequence = first.NextSequence!.Value,
                ThroughSequence = first.HeadSequence,
                MaxCount = 2
            },
            TestContext.Current.CancellationToken);
        Assert.Equal([3L, 4L], second.Events.Select(@event => @event.Sequence));
        Assert.Equal(4, second.NextSequence);

        var last = await fixture.Db.EventLog.ReadPageAsync(
            new EventLogReadOptions
            {
                AfterSequence = second.NextSequence!.Value,
                ThroughSequence = first.HeadSequence,
                MaxCount = 2
            },
            TestContext.Current.CancellationToken);
        Assert.Equal([5L], last.Events.Select(@event => @event.Sequence));
        Assert.False(last.HasMore);

        var bounded = await fixture.Db.EventLog.ReadPageAsync(
            new EventLogReadOptions { ThroughSequence = 3, MaxCount = 10 },
            TestContext.Current.CancellationToken);
        Assert.Equal([1L, 2L, 3L], bounded.Events.Select(@event => @event.Sequence));
        Assert.Equal(3, bounded.HeadSequence);
    }

    [Fact]
    public async Task ReadAsync_excludes_events_appended_after_enumeration_starts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AppendAsync("orders", Guid.NewGuid(), Guid.Empty, new OrderCreated(1), 1);
        await fixture.AppendAsync("orders", Guid.NewGuid(), Guid.Empty, new OrderCreated(2), 2);

        await using var enumerator = fixture.Db.EventLog.ReadAsync(
                new EventLogReadOptions { MaxCount = 1 },
                TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current.Sequence);

        await fixture.AppendAsync("orders", Guid.NewGuid(), Guid.Empty, new OrderCreated(3), 3);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Sequence);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task Filters_are_applied_before_paging_and_preserve_the_global_head()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await fixture.AppendAsync(
            "orders",
            Guid.NewGuid(),
            tenantA,
            new OrderCreated(1),
            1);
        await fixture.AppendAsync(
            "orders",
            Guid.NewGuid(),
            tenantB,
            new OrderCreated(2),
            2);
        await fixture.AppendAsync(
            "orders",
            Guid.NewGuid(),
            tenantA,
            new OrderCreated(3),
            3);
        await fixture.AppendAsync(
            "invoices",
            Guid.NewGuid(),
            tenantA,
            new InvoiceIssued(4),
            4);

        var page = await fixture.Db.EventLog.ReadPageAsync(
            new EventLogReadOptions
            {
                TenantId = tenantA,
                StreamTypes = ["orders"],
                EventTypes = ["order_created"],
                AfterSequence = 1,
                MaxCount = 1
            },
            TestContext.Current.CancellationToken);

        var @event = Assert.Single(page.Events);
        Assert.Equal(3, @event.Sequence);
        Assert.Equal("orders", @event.StreamType);
        Assert.Equal("order_created", @event.TypeName);
        Assert.Equal(4, page.HeadSequence);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Empty_filtered_ranges_return_the_global_head_for_checkpointing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AppendAsync("orders", Guid.NewGuid(), Guid.Empty, new OrderCreated(1), 1);
        await fixture.AppendAsync("orders", Guid.NewGuid(), Guid.Empty, new OrderCreated(2), 2);

        var page = await fixture.Db.EventLog.ReadPageAsync(
            new EventLogReadOptions { EventTypes = ["not_registered"] },
            TestContext.Current.CancellationToken);

        Assert.Empty(page.Events);
        Assert.Equal(2, page.HeadSequence);
        Assert.Null(page.NextSequence);
    }

    [Fact]
    public async Task Invalid_bounds_and_page_sizes_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Db.EventLog.ReadPageAsync(
                new EventLogReadOptions { AfterSequence = -1 },
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Db.EventLog.ReadPageAsync(
                new EventLogReadOptions { MaxCount = 0 },
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Db.EventLog.ReadPageAsync(
                new EventLogReadOptions { ThroughSequence = -1 },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Event_model_indexes_support_global_and_filtered_sequence_reads()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventType = fixture.Db.Model.FindEntityType(typeof(DbEvent))!;
        var indexes = eventType.GetIndexes().ToArray();

        Assert.Equal(
            [nameof(DbEvent.Sequence)],
            eventType.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(DbEvent.StreamId),
                    nameof(DbEvent.StreamType),
                    nameof(DbEvent.TenantId),
                    nameof(DbEvent.Version)
                ]));
        Assert.Contains(indexes, index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(DbEvent.TenantId), nameof(DbEvent.Sequence)]));
        Assert.Contains(indexes, index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(DbEvent.StreamType), nameof(DbEvent.Sequence)]));
        Assert.Contains(indexes, index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(DbEvent.TypeName), nameof(DbEvent.Sequence)]));
    }

    [Fact]
    public void ExistingDbContext_registers_the_global_reader_for_dependency_injection()
    {
        var services = new ServiceCollection();
        services.AddEventStore(builder => builder.ExistingDbContext<EventLogDbContext>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsAssignableFrom<IEventLogReader>(
            scope.ServiceProvider.GetRequiredService<IEventLogReader>());
    }

    private sealed record OrderCreated(int Number);

    private sealed record CustomerCreated(int Number);

    private sealed record InvoiceIssued(int Number);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, EventLogDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        internal EventLogDbContext Db { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var db = new EventLogDbContext(
                new DbContextOptionsBuilder<EventLogDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            return new Fixture(connection, db);
        }

        internal async Task AppendAsync(
            string streamType,
            Guid streamId,
            Guid tenantId,
            object @event,
            long sequence)
        {
            Db.Streams.StartStream(streamType, streamId, tenantId, @event);
            var stored = Db.ChangeTracker.Entries<DbEvent>()
                .Single(entry =>
                    entry.State == EntityState.Added &&
                    entry.Entity.StreamId == streamId)
                .Entity;
            stored.Sequence = sequence;

            await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class EventLogDbContext(DbContextOptions<EventLogDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);
    }
}
