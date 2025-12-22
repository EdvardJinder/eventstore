using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EventStoreCore.Tests.EventStoreFixture;

namespace EventStoreCore.Tests;

public class SubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ISubscription
    {
        public static List<IEvent> HandledEvents { get; } = new();
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

    [Fact]
    public async Task should_handle_events()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        });
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddSubscriptionDaemon<EventStoreDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));
            c.AddSubscription<TestSub>();
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        eventStoreDbContext.Database.EnsureCreated();

        var eventStore = eventStoreDbContext.Streams();
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetRequiredService<TestSub>();

        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
            .FindAsync(new object[] { subscription.GetType().AssemblyQualifiedName }, TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(1, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(TestSub.HandledEvents);
        Assert.IsType<TestEvent>(TestSub.HandledEvents[0].Data);
    }
}
