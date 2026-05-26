using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using EventStoreCore.TickerQ;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Linq.Expressions;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public class TickerQSchedulerTests : SchedulerContractTestsBase
{
    protected override ServiceProvider BuildProvider(Action<ISchedulerBuilder> configureScheduler)
    {
        var services = new ServiceCollection();
        var store = new TickerQTestStore();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        services.AddTickerQ(_ => { });
        services.AddSingleton(store);
        services.AddSingleton(clock);
        services.AddTransient<IScheduledJobHandler<PaymentTimeoutArgs>, PaymentTimeoutHandler>();
        services.AddSingleton(CreateTimeTickerManager(store));
        services.AddSingleton(CreatePersistenceProvider(store));
        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingTickerQ();
            configureScheduler(s);
        }));
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    protected override ISubscription GetSubscription(IServiceProvider provider)
    {
        return provider.GetServices<ISubscription>().OfType<TickerQSubscription>().Single();
    }

    protected override Task<string?> GetScheduledIdentityAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        return Task.FromResult(
            provider.GetRequiredService<TickerQTestStore>().FindByKey(key.Value).SingleOrDefault()?.Id.ToString("D"));
    }

    protected override Task<Guid?> GetScheduledSourceEventIdAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        var entity = provider.GetRequiredService<TickerQTestStore>().FindByKey(key.Value).SingleOrDefault();
        if (entity is null)
        {
            return Task.FromResult<Guid?>(null);
        }

        var envelope = TickerHelper.ReadTickerRequest<TickerQScheduledEnvelope>(entity.Request);
        return Task.FromResult<Guid?>(envelope.SourceEventId);
    }

    protected override Task<bool> ScheduleExistsAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct)
    {
        return Task.FromResult(provider.GetRequiredService<TickerQTestStore>().FindByKey(key.Value).Any());
    }

    protected override Task InvokeScheduledJobAsync(
        IServiceProvider provider,
        PaymentTimeoutArgs args,
        CancellationToken ct)
    {
        var store = provider.GetRequiredService<TickerQTestStore>();
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Description = "test",
            Function = TickerQConstants.FunctionName,
            Request = TickerHelper.CreateTickerRequest(new TickerQScheduledEnvelope(
                SourceEventId: args.SourceEventId,
                ArgumentType: ScheduledPayloadTypeIdentity.GetId(typeof(PaymentTimeoutArgs)),
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(args)))
        };

        store.Upsert(entity);
        return provider.GetRequiredService<TickerQScheduledJobDispatcher>().DispatchAsync(entity.Id, ct);
    }

    [Fact]
    public async Task should_store_version_tolerant_argument_type_identity()
    {
        var provider = BuildProvider(s =>
        {
            s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
                key: e => PaymentTimeoutKey(e.Data.OrderId),
                delay: _ => TimeSpan.FromMinutes(15),
                args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
        });

        var orderId = Guid.NewGuid();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var subscription = GetSubscription(provider);

        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        var entity = provider.GetRequiredService<TickerQTestStore>().FindByKey(PaymentTimeoutKey(orderId).Value).Single();
        var envelope = TickerHelper.ReadTickerRequest<TickerQScheduledEnvelope>(entity.Request);

        Assert.Equal(ScheduledPayloadTypeIdentity.GetId(typeof(PaymentTimeoutArgs)), envelope.ArgumentType);
        Assert.DoesNotContain("Version=", envelope.ArgumentType, StringComparison.Ordinal);
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

                store.Upsert(Clone(entity));
                return Task.FromResult((TickerResult<TimeTickerEntity>)null!);
            });

        manager.DeleteBatchAsync(Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                foreach (var id in call.Arg<List<Guid>>())
                {
                    store.Remove(id);
                }

                return Task.FromResult((TickerResult<TimeTickerEntity>)null!);
            });

        return manager;
    }

    private static ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> CreatePersistenceProvider(TickerQTestStore store)
    {
        var persistence = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        persistence.GetTimeTickers(Arg.Any<Expression<Func<TimeTickerEntity, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var predicate = call.Arg<Expression<Func<TimeTickerEntity, bool>>>().Compile();
                return Task.FromResult(store.All().Where(predicate).Select(Clone).ToArray());
            });

        persistence.GetTimeTickerRequest(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var entity = store.Find(call.Arg<Guid>());
                return Task.FromResult(entity?.Request);
            });

        return persistence;
    }

    private static TimeTickerEntity Clone(TimeTickerEntity entity)
    {
        return new TimeTickerEntity
        {
            Id = entity.Id,
            Description = entity.Description,
            ExecutionTime = entity.ExecutionTime,
            Function = entity.Function,
            Request = entity.Request,
            Retries = entity.Retries,
            RetryIntervals = entity.RetryIntervals
        };
    }

    private sealed class TickerQTestStore
    {
        private readonly Dictionary<Guid, TimeTickerEntity> _entities = [];

        public IEnumerable<TimeTickerEntity> All() => _entities.Values;

        public TimeTickerEntity? Find(Guid id) => _entities.GetValueOrDefault(id);

        public IEnumerable<TimeTickerEntity> FindByKey(string key) =>
            _entities.Values.Where(e => e.Function == TickerQConstants.FunctionName && e.Description == key);

        public void Remove(Guid id) => _entities.Remove(id);

        public void Upsert(TimeTickerEntity entity) => _entities[entity.Id] = entity;
    }
}
