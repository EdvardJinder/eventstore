using Azure.Messaging;
using IM.EventStore.CloudEvents;
using IM.EventStore.Persistence.EntityFrameworkCore;
using IM.EventStore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IM.EventStore.Tests.EventStoreFixture;

namespace IM.EventStore.Tests;
public class CloudEventSubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ICloudEventSubscription
    {
        public static List<CloudEvent> HandledEvents { get; } = new();
        public Task Handle(CloudEvent @event, CancellationToken ct)
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

    public class TestEvent2
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    [Fact]
    public async Task should_handle_events()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(
        c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddSubscriptionDaemon<EventStoreDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));

            c.AddCloudEventSubscription<TestSub>(c =>
            {
                c.MapEvent<TestEvent>(ievent =>
                {
                    var cloudEvent = new CloudEvent(
                        source: "urn:tests.subscriptions",
                        type: "com.im.eventstore.tests.subscriptions.testevent",
                        jsonSerializableData: ievent.Data,
                        dataSerializationType: ievent.EventType
                    )
                    {
                        Id = ievent.Id.ToString(),
                        Subject = $"tests/subscriptions/{ievent.Data.Id}",
                    };
                    return cloudEvent;
                });

                c.MapEvent<TestEvent2>("com.im.eventstore.tests.subscriptions.testevent2", "urn:tests.subscriptions", ievent => $"tests/subscriptions2/{ievent.Data.Id}");
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        eventStoreDbContext.Database.EnsureCreated();

        var eventStore = eventStoreDbContext.Streams();
        var streamId = Guid.NewGuid();

        List<object> events = [new TestEvent(), new TestEvent2()];
        eventStore.StartStream(streamId, events: events);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var subscriptionDeamon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();

        var subscription = provider.GetRequiredService<CloudEventSubscription<TestSub>>();

        var processed = await subscriptionDeamon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);
        var processed2 = await subscriptionDeamon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
            .FindAsync(new object[] { typeof(CloudEventSubscription<TestSub>).AssemblyQualifiedName! }, TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(2, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.True(processed2, "No event was processed");
        Assert.Equal(2, TestSub.HandledEvents.Count);

    }
}
