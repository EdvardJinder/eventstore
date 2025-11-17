using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IM.EventStore.Tests.EventStoreFixture;

namespace IM.EventStore.Tests;

public class SubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ISubscription
    {
        public static List<IEvent> HandledEvents { get; } = new();
        public static Task Handle(IEvent @event, IServiceProvider sp, CancellationToken ct)
        {
            // Handle the event (e.g., log it, process it, etc.)
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
        // Arrange
        var services = new ServiceCollection();
        services.AddEventStore<EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        },
        c =>
        {
            c.AddSubscriptionDaemon(_ => fixture.ConnectionString);
            c.AddSubscription<TestSub>();
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        eventStoreDbContext.Database.EnsureCreated();

        var eventStore = eventStoreDbContext.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var subscription = provider.GetRequiredService<Subscription<TestSub, EventStoreDbContext>>();

        var processed = await subscription.ProcessNextEventAsync(provider.CreateScope(), TestContext.Current.CancellationToken);

        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
            .FindAsync(new object[] { Subscription<TestSub, EventStoreDbContext>.Name }, TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(1, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(TestSub.HandledEvents);
        Assert.IsType<TestEvent>(TestSub.HandledEvents[0].Data);

    }
}
