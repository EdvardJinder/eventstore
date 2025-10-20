using Azure.Messaging;
using IM.EventStore.CloudEvents;
using IM.EventStore.EventGrid;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IM.EventStore.Tests.EventStoreFixture;

namespace IM.EventStore.Tests;

public class CloudEventSubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestSub : ICloudEventSubscription
    {
        public static List<CloudEvent> HandledEvents { get; } = new();
        public static Task Handle(CloudEvent @event, IServiceProvider sp, CancellationToken ct)
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
            c.AddSubscriptionDaemon(fixture.ConnectionString);
            c.AddCloudEventSubscription<TestSub>(c=>
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
            });

            
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var eventStoreDbContext = provider.GetRequiredService<EventStoreDbContext>();
        eventStoreDbContext.Database.EnsureCreated();

        var eventStore = eventStoreDbContext.Streams();
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var subscription = provider.GetRequiredService<Subscription<CloudEventSubscription<TestSub>, EventStoreDbContext>>();

        var processed = await subscription.ProcessNextEventAsync(provider.CreateScope(), TestContext.Current.CancellationToken);

        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
            .FindAsync(new object[] { typeof(CloudEventSubscription<TestSub>).AssemblyQualifiedName! }, TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(1, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(TestSub.HandledEvents);

        var handledEvent = TestSub.HandledEvents[0];

        var testEventData = handledEvent.Data.ToObjectFromJson<TestEvent>(System.Text.Json.JsonSerializerOptions.Default);

        Assert.NotNull(testEventData);
        Assert.IsType<TestEvent>(testEventData);

    }
}
