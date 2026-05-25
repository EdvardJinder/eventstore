using EventStoreCore.Abstractions;
using EventStoreCore;
using EventStoreCore.Postgres;

using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EventStoreCore.Tests.EventStoreFixture;

namespace EventStoreCore.Tests;

public class SubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class DeliveryTrackingDbContext : DbContext
    {
        public DeliveryTrackingDbContext(DbContextOptions<DeliveryTrackingDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseEventStore();
            modelBuilder.Entity<ProcessedEventRecord>(entity =>
            {
                entity.HasKey(e => e.EventId);
            });
        }
    }

    public class TestSub : ISubscription
    {
        public List<IEvent> HandledEvents { get; } = new();
        public Task Handle(IEvent @event, CancellationToken ct)
        {
            HandledEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    public class TestSub2 : ISubscription
    {
        public List<IEvent> HandledEvents { get; } = new();
        public Task Handle(IEvent @event, CancellationToken ct)
        {
            HandledEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    public class TestEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    public sealed class RetryTrackingSubscription : ISubscription
    {
        public static List<Guid> HandledEventIds { get; } = new();
        public static int Attempts { get; private set; }

        public Task Handle(IEvent @event, CancellationToken ct)
        {
            Attempts++;
            HandledEventIds.Add(@event.Id);

            if (Attempts == 1)
            {
                throw new InvalidOperationException("Simulated subscription failure");
            }

            return Task.CompletedTask;
        }

        public static void Reset()
        {
            Attempts = 0;
            HandledEventIds.Clear();
        }
    }

    public sealed class ProcessedEventRecord
    {
        public Guid EventId { get; set; }
    }

    public sealed class DeduplicatingScopedSubscription : IScopedSubscription
    {
        public static List<Guid> DeliveredEventIds { get; } = new();

        public Task Handle(IEvent @event, CancellationToken ct)
        {
            throw new NotSupportedException("Use HandleAsync for scoped subscription execution.");
        }

        public async Task HandleAsync(DbContext dbContext, IEvent @event, CancellationToken ct)
        {
            DeliveredEventIds.Add(@event.Id);

            var processedEvents = dbContext.Set<ProcessedEventRecord>();
            if (await processedEvents.AnyAsync(e => e.EventId == @event.Id, ct))
            {
                return;
            }

            processedEvents.Add(new ProcessedEventRecord
            {
                EventId = @event.Id
            });
        }

        public static void Reset()
        {
            DeliveredEventIds.Clear();
        }
    }

    private ServiceProvider BuildProvider<TDbContext>(Action<IEventStoreBuilder> configure)
        where TDbContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddDbContext<TDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        });
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<TDbContext>();
            c.AddSubscriptionDaemon<TDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));
            configure(c);
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

    private static async Task ResetDeliveryTrackingDatabaseAsync(DeliveryTrackingDbContext dbContext, CancellationToken ct)
    {
        await dbContext.Set<ProcessedEventRecord>().ExecuteDeleteAsync(ct);
        await dbContext.Set<DbSubscription>().ExecuteDeleteAsync(ct);
        await dbContext.Events.ExecuteDeleteAsync(ct);
        await dbContext.Set<DbStream>().ExecuteDeleteAsync(ct);
    }


    [Fact]
    public async Task should_handle_events()
    {
        var provider = BuildProvider<EventStoreDbContext>(c => c.AddSubscription<TestSub>());
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        await eventStoreDbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await eventStoreDbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDatabaseAsync(eventStoreDbContext, TestContext.Current.CancellationToken);

        var eventStore = eventStoreDbContext.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var persistedEvent = await eventStoreDbContext.Events
            .AsNoTracking()
            .SingleAsync(e => e.StreamId == streamId, TestContext.Current.CancellationToken);

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetRequiredService<TestSub>();

        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        var subscriptionEntity = await FindGlobalSubscriptionAsync(
            eventStoreDbContext,
            subscription.GetType().AssemblyQualifiedName!);


        Assert.NotNull(subscriptionEntity);
        Assert.Equal(persistedEvent.Sequence, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(subscription.HandledEvents);
        Assert.IsType<TestEvent>(subscription.HandledEvents[0].Data);
    }

    [Fact]
    public async Task should_create_subscription_rows_for_multiple_subscriptions()
    {
        var provider = BuildProvider<EventStoreDbContext>(c =>
        {
            c.AddSubscription<TestSub>();
            c.AddSubscription<TestSub2>();
        });
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        await eventStoreDbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await eventStoreDbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDatabaseAsync(eventStoreDbContext, TestContext.Current.CancellationToken);

        var eventStore = eventStoreDbContext.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription1 = provider.GetRequiredService<TestSub>();
        var subscription2 = provider.GetRequiredService<TestSub2>();

        var processed1 = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription1, TestContext.Current.CancellationToken);
        var processed2 = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription2, TestContext.Current.CancellationToken);

        var subscriptionEntity1 = await FindGlobalSubscriptionAsync(
            eventStoreDbContext,
            subscription1.GetType().AssemblyQualifiedName!);

        var subscriptionEntity2 = await FindGlobalSubscriptionAsync(
            eventStoreDbContext,
            subscription2.GetType().AssemblyQualifiedName!);

        Assert.NotNull(subscriptionEntity1);
        Assert.NotNull(subscriptionEntity2);
        Assert.True(subscriptionEntity1.Sequence > 0);
        Assert.True(subscriptionEntity2.Sequence > 0);
        Assert.True(processed1, "No event was processed");
        Assert.True(processed2, "No event was processed");
        Assert.Single(subscription1.HandledEvents);
        Assert.Single(subscription2.HandledEvents);
        Assert.IsType<TestEvent>(subscription1.HandledEvents[0].Data);
        Assert.IsType<TestEvent>(subscription2.HandledEvents[0].Data);

    }

    [Fact]
    public async Task failed_subscription_delivery_is_retried_with_the_same_event_id()
    {
        RetryTrackingSubscription.Reset();

        var provider = BuildProvider<EventStoreDbContext>(c => c.AddSubscription<RetryTrackingSubscription>());
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        await eventStoreDbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await eventStoreDbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDatabaseAsync(eventStoreDbContext, TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        eventStoreDbContext.Streams.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var expectedEvent = await eventStoreDbContext.Events
            .AsNoTracking()
            .SingleAsync(e => e.StreamId == streamId, TestContext.Current.CancellationToken);

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetRequiredService<RetryTrackingSubscription>();
        var manager = provider.GetRequiredService<ISubscriptionManager>();
        var subscriptionName = typeof(RetryTrackingSubscription).AssemblyQualifiedName!;

        var firstProcessed = await daemon.ProcessNextEventAsync(
            provider.CreateScope(),
            subscription,
            TestContext.Current.CancellationToken);

        var failedSubscriptionRow = await eventStoreDbContext.Set<DbSubscription>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.SubscriptionAssemblyQualifiedName == subscriptionName &&
                    e.CheckpointScope == CheckpointScope.Global &&
                    e.TenantId == Guid.Empty,
                TestContext.Current.CancellationToken);

        Assert.False(firstProcessed);
        Assert.NotNull(failedSubscriptionRow);
        Assert.Equal(SubscriptionState.Faulted, failedSubscriptionRow!.State);
        Assert.Equal(expectedEvent.Sequence, failedSubscriptionRow.FailedEventSequence);
        Assert.Single(RetryTrackingSubscription.HandledEventIds);
        Assert.Equal(expectedEvent.EventId, RetryTrackingSubscription.HandledEventIds[0]);

        await manager.RetryFailedEventAsync(subscriptionName, TestContext.Current.CancellationToken);

        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        var succeededSubscriptionRow = await eventStoreDbContext.Set<DbSubscription>()
            .AsNoTracking()
            .SingleAsync(
                e => e.SubscriptionAssemblyQualifiedName == subscriptionName &&
                    e.CheckpointScope == CheckpointScope.Global &&
                    e.TenantId == Guid.Empty,
                TestContext.Current.CancellationToken);

        Assert.True(processed);
        Assert.Equal(2, RetryTrackingSubscription.Attempts);
        Assert.Equal(expectedEvent.EventId, RetryTrackingSubscription.HandledEventIds[1]);
        Assert.Equal(expectedEvent.Sequence, succeededSubscriptionRow.Sequence);
        Assert.Equal(SubscriptionState.Active, succeededSubscriptionRow.State);
    }

    [Fact]
    public async Task replay_redelivers_the_same_event_id_so_consumers_can_deduplicate()
    {
        DeduplicatingScopedSubscription.Reset();

        var provider = BuildProvider<DeliveryTrackingDbContext>(c => c.AddSubscription<DeduplicatingScopedSubscription>());
        var dbContext = provider.GetRequiredService<DeliveryTrackingDbContext>();
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await ResetDeliveryTrackingDatabaseAsync(dbContext, TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        dbContext.Streams.StartStream(streamId, events: [new TestEvent()]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var daemon = provider.GetRequiredService<SubscriptionDaemon<DeliveryTrackingDbContext>>();
        var subscription = provider.GetRequiredService<DeduplicatingScopedSubscription>();
        var manager = provider.GetRequiredService<ISubscriptionManager>();
        var subscriptionName = typeof(DeduplicatingScopedSubscription).AssemblyQualifiedName!;

        Assert.True(await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken));

        await manager.ReplayAsync(subscriptionName, startSequence: 1, ct: TestContext.Current.CancellationToken);

        Assert.True(await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken));

        var processedEventIds = await dbContext.Set<ProcessedEventRecord>()
            .AsNoTracking()
            .Select(e => e.EventId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, DeduplicatingScopedSubscription.DeliveredEventIds.Count);
        Assert.All(DeduplicatingScopedSubscription.DeliveredEventIds, id => Assert.Equal(DeduplicatingScopedSubscription.DeliveredEventIds[0], id));
        Assert.Single(processedEventIds);
        Assert.Equal(DeduplicatingScopedSubscription.DeliveredEventIds[0], processedEventIds[0]);
    }

    private static Task<DbSubscription?> FindGlobalSubscriptionAsync(EventStoreDbContext db, string name)
    {
        return db.Set<DbSubscription>()
            .FirstOrDefaultAsync(s =>
                s.SubscriptionAssemblyQualifiedName == name &&
                s.CheckpointScope == CheckpointScope.Global &&
                s.TenantId == Guid.Empty,
                TestContext.Current.CancellationToken);
    }
}

