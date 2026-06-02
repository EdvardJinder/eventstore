using EventStoreCore.Abstractions;
using EventStoreCore.TickerQ;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public sealed class TickerQExecutionTests
{
    [Fact]
    public async Task should_execute_scheduled_tickerq_job()
    {
        TickerQExecutionProbe.Reset();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTickerQ(_ => { });
        builder.Services.AddLogging();
        builder.Services.AddEventStore(eventStore => eventStore.AddScheduler(s =>
        {
            s.UsingTickerQ();
            s.On<OrderPlaced>().TickerQ("tickerq-execution", static async (e, manager, _, ct) =>
            {
                await manager.AddAsync(new TimeTickerEntity
                {
                    Id = e.Data.OrderId,
                    Function = TickerQExecutionProbe.FunctionName,
                    ExecutionTime = DateTime.UtcNow
                }, ct);
            });
        }));

        await using var app = builder.Build();
        app.UseTickerQ();

        var subscription = app.Services.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await app.StartAsync(TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        await SchedulerTestWait.WaitForAsync(() => TickerQExecutionProbe.Executed.Contains(placed.Data.OrderId), TimeSpan.FromSeconds(5));
        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}

public sealed class TickerQExecutionProbe
{
    public const string FunctionName = "TickerQExecutionProbe";

    private static readonly object Gate = new();

    public static List<Guid> Executed { get; } = [];

    [TickerFunction(FunctionName)]
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken ct)
    {
        if (context.Id is { } orderId)
        {
            lock (Gate)
            {
                Executed.Add(orderId);
            }
        }

        return Task.CompletedTask;
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Executed.Clear();
        }
    }
}
