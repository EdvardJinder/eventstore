using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Postgres;

using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EventStoreCore.Tests.EventStoreFixture;

namespace EventStoreCore.Tests;

public class SubscriptionManagerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    public class TestEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString));

        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddSubscriptionDaemon<EventStoreDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));
            c.AddSubscription<TestSub>();
        });

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task ResetDatabaseAsync(EventStoreDbContext dbContext, CancellationToken ct)
    {
        await dbContext.Set<DbSubscription>().ExecuteDeleteAsync(ct);
        await dbContext.Events.ExecuteDeleteAsync(ct);
        await dbContext.Set<DbStream>().ExecuteDeleteAsync(ct);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsDefaultStatus_WhenSubscriptionRegisteredButNotInitialized()
    {
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDatabaseAsync(db, TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<ISubscriptionManager>();
        var name = typeof(TestSub).AssemblyQualifiedName!;

        var status = await manager.GetStatusAsync(name, TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(name, status!.SubscriptionName);
        Assert.Equal(0, status.Position);
    }

    [Fact]
    public async Task ReplayAsync_ResetsSequenceToStartSequence()
    {
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDatabaseAsync(db, TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        db.Streams.StartStream(streamId, events: [new TestEvent(), new TestEvent()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<ISubscriptionManager>();
        var name = typeof(TestSub).AssemblyQualifiedName!;

        await manager.ReplayAsync(name, startSequence: 2, ct: TestContext.Current.CancellationToken);

        var subscription = await db.Set<DbSubscription>()
            .FindAsync(new object[] { name }, TestContext.Current.CancellationToken);

        Assert.NotNull(subscription);
        Assert.Equal(1, subscription!.Sequence);
    }

    [Fact]
    public async Task ReplayAsync_ResetsSequenceFromTimestamp()
    {
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDatabaseAsync(db, TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        db.Streams.StartStream(streamId, events: [new TestEvent(), new TestEvent()]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dbEvents = await db.Events
            .OrderBy(e => e.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);

        var baseTime = DateTimeOffset.UtcNow;
        dbEvents[0].Timestamp = baseTime.AddMinutes(-10);
        dbEvents[1].Timestamp = baseTime.AddMinutes(-1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<ISubscriptionManager>();
        var name = typeof(TestSub).AssemblyQualifiedName!;

        await manager.ReplayAsync(name, fromTimestamp: dbEvents[1].Timestamp, ct: TestContext.Current.CancellationToken);

        var subscription = await db.Set<DbSubscription>()
            .FindAsync(new object[] { name }, TestContext.Current.CancellationToken);

        Assert.NotNull(subscription);
        Assert.Equal(dbEvents[1].Sequence - 1, subscription!.Sequence);
    }
}
