using System;
using System.Threading;
using System.Threading.Tasks;
using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IM.EventStore.Tests;

public class SubscriptionLockTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ISubscription
    {
        public static Task Handle(IEvent @event, IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;
    }
    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    }

    [Fact]
    public async Task AcquireSubscriptionLockAsync_ReturnsHandle_WhenLockFree()
    {
        var services = new ServiceCollection();

        services.AddEventStore<TestDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        }, c =>
        {
            c.AddSubscriptionDaemon(_ => fixture.ConnectionString);
            c.AddSubscription<TestSub>();
        });

        services.AddLogging();

        var provider = services.BuildServiceProvider();

        var subscription = provider.GetRequiredService<Subscription<TestSub, TestDbContext>>();

        var handle = await subscription.AcquireSubscriptionLockAsync(CancellationToken.None);

        Assert.NotNull(handle);

        await (handle?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    [Fact]
    public async Task AcquireSubscriptionLockAsync_SecondInstanceCancelled_WhenFirstHoldsLock()
    {
        // First service provider with a held lock
        var services1 = new ServiceCollection();
        services1.AddEventStore<TestDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        }, c =>
        {
            c.AddSubscriptionDaemon(_ => fixture.ConnectionString);
            c.AddSubscription<TestSub>();
        });
        services1.AddLogging();
        var provider1 = services1.BuildServiceProvider();
        var subscription1 = provider1.GetRequiredService<Subscription<TestSub, TestDbContext>>();

        var handle1 = await subscription1.AcquireSubscriptionLockAsync(CancellationToken.None);
        Assert.NotNull(handle1);

        // Second service provider attempting to acquire the same lock
        var services2 = new ServiceCollection();
        services2.AddEventStore<TestDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        }, c =>
        {
            c.AddSubscriptionDaemon(_ => fixture.ConnectionString);
            c.AddSubscription<TestSub>();
        });
        services2.AddLogging();
        var provider2 = services2.BuildServiceProvider();
        var subscription2 = provider2.GetRequiredService<Subscription<TestSub, TestDbContext>>();

        using var cts = new CancellationTokenSource(1_000); // cancel after 1s

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await subscription2.AcquireSubscriptionLockAsync(cts.Token);
        });

        await handle1.DisposeAsync();
    }
}
