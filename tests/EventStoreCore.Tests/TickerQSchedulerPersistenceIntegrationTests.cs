using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using EventStoreCore.TickerQ;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public sealed class TickerQSchedulerPersistenceIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options =>
        {
            options.DisableBackgroundServices();
            options.AddOperationalStore(efOptions =>
            {
                efOptions.UseTickerQDbContext<TickerQDbContext>(db => db.UseSqlite(_connection));
            });
        });
        services.AddTransient<IScheduledJobHandler<SchedulerContractTestsBase.PaymentTimeoutArgs>, SchedulerContractTestsBase.PaymentTimeoutHandler>();
        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingTickerQ();
            s.Schedule<SchedulerContractTestsBase.OrderPlaced, SchedulerContractTestsBase.PaymentTimeoutArgs>(
                key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"),
                delay: _ => TimeSpan.FromMinutes(15),
                args: e => new SchedulerContractTestsBase.PaymentTimeoutArgs(e.Data.OrderId, e.Id));
            s.Cancel<SchedulerContractTestsBase.PaymentCaptured>(
                key: e => ScheduleKey.Create($"payment-timeout:{e.Data.OrderId}"));
        }));

        _provider = services.BuildServiceProvider();
        TickerFunctionProvider.Build();

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_persist_single_time_ticker_for_replayed_event()
    {
        var orderId = Guid.NewGuid();
        var placed = CreateEvent(new SchedulerContractTestsBase.OrderPlaced { OrderId = orderId });
        var subscription = _provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
        var rows = await dbContext.Set<TimeTickerEntity>()
            .Where(t => t.Function == TickerQConstants.FunctionName && t.Description == $"payment-timeout:{orderId}")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(rows);
    }

    [Fact]
    public async Task should_delete_persisted_time_ticker_on_cancel()
    {
        var orderId = Guid.NewGuid();
        var placed = CreateEvent(new SchedulerContractTestsBase.OrderPlaced { OrderId = orderId });
        var captured = CreateEvent(new SchedulerContractTestsBase.PaymentCaptured { OrderId = orderId });
        var subscription = _provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(captured, TestContext.Current.CancellationToken);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
        var rows = await dbContext.Set<TimeTickerEntity>()
            .Where(t => t.Function == TickerQConstants.FunctionName && t.Description == $"payment-timeout:{orderId}")
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task should_dispatch_persisted_request_to_di_handler()
    {
        SchedulerContractTestsBase.PaymentTimeoutHandler.Reset();

        var orderId = Guid.NewGuid();
        var placed = CreateEvent(new SchedulerContractTestsBase.OrderPlaced { OrderId = orderId });
        var subscription = _provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();

        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Guid tickerId;
        await using (var scope = _provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
            tickerId = await dbContext.Set<TimeTickerEntity>()
                .Where(t => t.Function == TickerQConstants.FunctionName && t.Description == $"payment-timeout:{orderId}")
                .Select(t => t.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var dispatcher = _provider.GetRequiredService<TickerQScheduledJobDispatcher>();
        await dispatcher.DispatchAsync(tickerId, TestContext.Current.CancellationToken);

        Assert.Single(SchedulerContractTestsBase.PaymentTimeoutHandler.Executed);
        Assert.Equal(orderId, SchedulerContractTestsBase.PaymentTimeoutHandler.Executed[0].OrderId);
        Assert.Equal(placed.Id, SchedulerContractTestsBase.PaymentTimeoutHandler.Executed[0].SourceEventId);
    }

    private static IEvent<T> CreateEvent<T>(T data)
        where T : class
    {
        return new TestEvent<T>(Guid.NewGuid(), data);
    }

    private sealed class TestEvent<T>(Guid id, T data) : IEvent<T>
        where T : class
    {
        public Guid Id { get; } = id;
        public long Version => 1;
        public T Data { get; } = data;
        object IEvent.Data => Data;
        public Guid StreamId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public Guid TenantId { get; } = Guid.Empty;
        public Type EventType => typeof(T);
    }
}
