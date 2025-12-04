using IM.EventStore.Abstractions;
using IM.EventStore.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.Tests;

public class SubscriptionLockTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) => Task.CompletedTask;
    }
    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    }

    [Fact]
    public async Task AcquireSubscriptionLockAsync_ReturnsHandle_WhenLockFree()
    {
        var services = new ServiceCollection();

        services.AddEventStore(c =>
        {
            c.UsingPostgres<TestDbContext>((sp, options) =>
            {
                options.UseNpgsql(fixture.ConnectionString);
            }, c => c.AddSubscriptionDaemon(_ => fixture.ConnectionString));

            c.AddSubscription<TestSub>();
        });

        services.AddLogging();

        var provider = services.BuildServiceProvider();
        
        var daemon = provider.GetRequiredService<SubscriptionDaemon<TestDbContext>>();
        var subscription = provider.GetRequiredService<TestSub>();

        var handle = await daemon.AcquireSubscriptionLockAsync<TestSub>(CancellationToken.None);

        Assert.NotNull(handle);

        await (handle?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    [Fact]
    public async Task AcquireSubscriptionLockAsync_SecondInstanceCancelled_WhenFirstHoldsLock()
    {
        // First service provider with a held lock
        var services1 = new ServiceCollection();
        services1.AddEventStore(c =>
        {
            c.UsingPostgres<TestDbContext>((sp, options) =>
            {
                options.UseNpgsql(fixture.ConnectionString);
            }, c => c.AddSubscriptionDaemon(_ => fixture.ConnectionString));

            c.AddSubscription<TestSub>();
        });
        services1.AddLogging();
        var provider1 = services1.BuildServiceProvider();
        var daemon = provider1.GetRequiredService<SubscriptionDaemon<TestDbContext>>();
        var subscription1 = provider1.GetRequiredService<TestSub>();

        var handle1 = await daemon.AcquireSubscriptionLockAsync<TestSub>(CancellationToken.None);
        Assert.NotNull(handle1);

        var handle2 = await daemon.AcquireSubscriptionLockAsync<TestSub>(CancellationToken.None);
        Assert.Null(handle2);

        await handle1.DisposeAsync();
    }
}
