using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public abstract class SchedulerContractTestsBase
{
    public sealed record PaymentTimeoutArgs(Guid OrderId, Guid SourceEventId);

    public sealed class OrderPlaced
    {
        public Guid OrderId { get; init; }
    }

    public sealed class PaymentDeadlineChanged
    {
        public Guid OrderId { get; init; }
    }

    public sealed class PaymentCaptured
    {
        public Guid OrderId { get; init; }
    }

    public sealed class PaymentTimeoutHandler : IScheduledJobHandler<PaymentTimeoutArgs>
    {
        public static List<PaymentTimeoutArgs> Executed { get; } = [];

        public Task HandleAsync(PaymentTimeoutArgs args, CancellationToken ct)
        {
            Executed.Add(args);
            return Task.CompletedTask;
        }

        public static void Reset() => Executed.Clear();
    }

    protected sealed class TestEvent<T>(
        Guid id,
        T data,
        Guid? streamId = null,
        DateTimeOffset? timestamp = null,
        Guid? tenantId = null) : IEvent<T>
        where T : class
    {
        public Guid Id { get; } = id;
        public long Version => 1;
        public T Data { get; } = data;
        object IEvent.Data => Data;
        public Guid StreamId { get; } = streamId ?? Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = timestamp ?? DateTimeOffset.UtcNow;
        public Guid TenantId { get; } = tenantId ?? Guid.Empty;
        public Type EventType => typeof(T);
    }

    protected static ScheduleKey PaymentTimeoutKey(Guid orderId) => ScheduleKey.Create($"payment-timeout:{orderId}");

    protected abstract ServiceProvider BuildProvider(Action<ISchedulerBuilder> configureScheduler);

    protected abstract ISubscription GetSubscription(IServiceProvider provider);

    protected abstract Task<string?> GetScheduledIdentityAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct);

    protected abstract Task<Guid?> GetScheduledSourceEventIdAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct);

    protected abstract Task<bool> ScheduleExistsAsync(
        IServiceProvider provider,
        ScheduleKey key,
        CancellationToken ct);

    protected abstract Task InvokeScheduledJobAsync(
        IServiceProvider provider,
        PaymentTimeoutArgs args,
        CancellationToken ct);

    [Fact]
    public async Task should_schedule_job_from_event_and_replay_same_event_without_duplicate_schedule()
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
        var key = PaymentTimeoutKey(orderId);

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        var initialIdentity = await GetScheduledIdentityAsync(provider, key, TestContext.Current.CancellationToken);
        var initialSourceEventId = await GetScheduledSourceEventIdAsync(provider, key, TestContext.Current.CancellationToken);

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        var replayIdentity = await GetScheduledIdentityAsync(provider, key, TestContext.Current.CancellationToken);
        var replaySourceEventId = await GetScheduledSourceEventIdAsync(provider, key, TestContext.Current.CancellationToken);

        Assert.NotNull(initialIdentity);
        Assert.Equal(initialIdentity, replayIdentity);
        Assert.Equal(placed.Id, initialSourceEventId);
        Assert.Equal(placed.Id, replaySourceEventId);
    }

    [Fact]
    public async Task should_replace_existing_schedule_when_new_event_uses_same_key()
    {
        var provider = BuildProvider(s =>
        {
            s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
                key: e => PaymentTimeoutKey(e.Data.OrderId),
                delay: _ => TimeSpan.FromMinutes(15),
                args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
            s.Schedule<PaymentDeadlineChanged, PaymentTimeoutArgs>(
                key: e => PaymentTimeoutKey(e.Data.OrderId),
                delay: _ => TimeSpan.FromMinutes(30),
                args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
        });

        var orderId = Guid.NewGuid();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var deadlineChanged = new TestEvent<PaymentDeadlineChanged>(Guid.NewGuid(), new PaymentDeadlineChanged { OrderId = orderId });
        var subscription = GetSubscription(provider);
        var key = PaymentTimeoutKey(orderId);

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        var firstSourceEventId = await GetScheduledSourceEventIdAsync(provider, key, TestContext.Current.CancellationToken);

        await subscription.Handle(deadlineChanged, TestContext.Current.CancellationToken);
        var secondSourceEventId = await GetScheduledSourceEventIdAsync(provider, key, TestContext.Current.CancellationToken);

        Assert.Equal(placed.Id, firstSourceEventId);
        Assert.Equal(deadlineChanged.Id, secondSourceEventId);
    }

    [Fact]
    public async Task should_cancel_scheduled_job_for_matching_key()
    {
        var provider = BuildProvider(s =>
        {
            s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
                key: e => PaymentTimeoutKey(e.Data.OrderId),
                delay: _ => TimeSpan.FromMinutes(15),
                args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
            s.Cancel<PaymentCaptured>(e => PaymentTimeoutKey(e.Data.OrderId));
        });

        var orderId = Guid.NewGuid();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = orderId });
        var captured = new TestEvent<PaymentCaptured>(Guid.NewGuid(), new PaymentCaptured { OrderId = orderId });
        var subscription = GetSubscription(provider);
        var key = PaymentTimeoutKey(orderId);

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        Assert.True(await ScheduleExistsAsync(provider, key, TestContext.Current.CancellationToken));

        await subscription.Handle(captured, TestContext.Current.CancellationToken);

        Assert.False(await ScheduleExistsAsync(provider, key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task should_ignore_cancel_for_missing_key()
    {
        var provider = BuildProvider(s =>
        {
            s.Cancel<PaymentCaptured>(e => PaymentTimeoutKey(e.Data.OrderId));
        });

        var captured = new TestEvent<PaymentCaptured>(Guid.NewGuid(), new PaymentCaptured { OrderId = Guid.NewGuid() });
        var subscription = GetSubscription(provider);

        await subscription.Handle(captured, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_resolve_job_handlers_from_di()
    {
        PaymentTimeoutHandler.Reset();

        var provider = BuildProvider(s =>
        {
            s.Schedule<OrderPlaced, PaymentTimeoutArgs>(
                key: e => PaymentTimeoutKey(e.Data.OrderId),
                delay: _ => TimeSpan.FromMinutes(15),
                args: e => new PaymentTimeoutArgs(e.Data.OrderId, e.Id));
        });

        var args = new PaymentTimeoutArgs(Guid.NewGuid(), Guid.NewGuid());

        await InvokeScheduledJobAsync(provider, args, TestContext.Current.CancellationToken);

        Assert.Single(PaymentTimeoutHandler.Executed);
        Assert.Equal(args, PaymentTimeoutHandler.Executed[0]);
    }
}
