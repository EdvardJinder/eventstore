using EventStoreCore.Abstractions;
using EventStoreCore.TickerQ;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public class TickerQSchedulerTests
{
    [Fact]
    public async Task should_invoke_tickerq_action_once_for_replayed_event()
    {
        var store = new TickerQTestStore();
        var provider = BuildProvider(store, s =>
        {
            s.On<OrderPlaced>().TickerQ(static async (e, manager, _, ct) =>
            {
                await manager.AddAsync(new TimeTickerEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "PaymentTimeout",
                    Description = $"payment-timeout:{e.Data.OrderId}",
                    ExecutionTime = DateTime.UtcNow.AddMinutes(15)
                }, ct);
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Single(store.All());
    }

    [Fact]
    public async Task should_run_each_registration_for_same_event_and_args()
    {
        var store = new TickerQTestStore();
        var provider = BuildProvider(store, s =>
        {
            s.On<OrderPlaced>().TickerQ("payment-reminder", static async (e, manager, _, ct) =>
            {
                await manager.AddAsync(new TimeTickerEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "PaymentReminder",
                    Description = $"payment:{e.Data.OrderId}"
                }, ct);
            });
            s.On<OrderPlaced>().TickerQ("payment-escalation", static async (e, manager, _, ct) =>
            {
                await manager.AddAsync(new TimeTickerEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "PaymentEscalation",
                    Description = $"payment:{e.Data.OrderId}"
                }, ct);
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Equal(2, store.All().Count());
    }

    [Fact]
    public async Task should_allow_action_to_use_tickerq_replace_semantics()
    {
        var store = new TickerQTestStore();
        var provider = BuildProvider(store, s =>
        {
            s.On<OrderPlaced>().TickerQ(static async (e, manager, sp, ct) =>
            {
                var store = sp.GetRequiredService<TickerQTestStore>();
                foreach (var existing in store.FindByDescription($"payment-timeout:{e.Data.OrderId}"))
                {
                    await manager.DeleteAsync(existing.Id, ct);
                }

                await manager.AddAsync(new TimeTickerEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "PaymentTimeout",
                    Description = $"payment-timeout:{e.Data.OrderId}",
                    ExecutionTime = DateTime.UtcNow.AddMinutes(30)
                }, ct);
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();
        var orderId = Guid.NewGuid();
        var first = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var second = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });

        await subscription.Handle(first, TestContext.Current.CancellationToken);
        await subscription.Handle(second, TestContext.Current.CancellationToken);

        Assert.Single(store.FindByDescription($"payment-timeout:{orderId}"));
    }

    private static ServiceProvider BuildProvider(TickerQTestStore store, Action<EventStoreCore.Scheduling.ISchedulerBuilder> configureScheduler)
    {
        var services = new ServiceCollection();
        services.AddTickerQ(_ => { });
        services.AddSingleton(store);
        services.AddSingleton(CreateTimeTickerManager(store));
        services.AddLogging();
        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingTickerQ();
            configureScheduler(s);
        }));

        return services.BuildServiceProvider();
    }

    private static ITimeTickerManager<TimeTickerEntity> CreateTimeTickerManager(TickerQTestStore store)
    {
        var manager = Substitute.For<ITimeTickerManager<TimeTickerEntity>>();

        manager.AddAsync(Arg.Any<TimeTickerEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var entity = call.Arg<TimeTickerEntity>();
                if (entity.Id == Guid.Empty)
                {
                    entity.Id = Guid.NewGuid();
                }

                store.Upsert(entity);
                return Task.FromResult((TickerResult<TimeTickerEntity>)null!);
            });

        manager.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                store.Remove(call.Arg<Guid>());
                return Task.FromResult((TickerResult<TimeTickerEntity>) null!);
            });

        return manager;
    }

    private sealed class TickerQTestStore
    {
        private readonly Dictionary<Guid, TimeTickerEntity> _entities = [];

        public IEnumerable<TimeTickerEntity> All() => _entities.Values;

        public IEnumerable<TimeTickerEntity> FindByDescription(string description) =>
            _entities.Values.Where(e => e.Description == description).ToArray();

        public void Remove(Guid id) => _entities.Remove(id);

        public void Upsert(TimeTickerEntity entity) => _entities[entity.Id] = entity;
    }
}
