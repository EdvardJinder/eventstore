using IM.EventStore.MassTransit;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static IM.EventStore.Tests.EventStoreFixture;

namespace IM.EventStore.Tests;

public class MassTransitSubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestConsumer : IConsumer<EventContext<TestEvent>>
    {
        public static List<EventContext<TestEvent>> HandledEvents { get; } = new();
        public Task Consume(ConsumeContext<EventContext<TestEvent>> context)
        {
            HandledEvents.Add(context.Message);
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
                c.AddMassTransitEventStoreSubscription();
            });

        services.RemoveAll<IHostedService>();

        services.AddMassTransitTestHarness(c =>
        {
            c.AddConsumer<TestConsumer>();
        });

        services.AddLogging();


        var provider = services.BuildServiceProvider(true);

        var scope = provider.CreateScope();

        var eventStoreDbContext = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();

        eventStoreDbContext.Database.EnsureCreated();

        var eventStore = eventStoreDbContext.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var harness = provider.GetTestHarness();
        await harness.Start();

        var consumer = harness.GetConsumerHarness<TestConsumer>();

        var subscription = provider.GetRequiredService<Subscription<MassTransitSubscription, EventStoreDbContext>>();
        
        var processed = await subscription.ProcessNextEventAsync(provider.CreateScope(), TestContext.Current.CancellationToken);

        Assert.True(await consumer.Consumed.Any<EventContext<TestEvent>>(TestContext.Current.CancellationToken));
      
        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
          .FindAsync([Subscription<MassTransitSubscription, EventStoreDbContext>.Name], TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(1, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(TestConsumer.HandledEvents);
        Assert.IsType<TestEvent>(TestConsumer.HandledEvents[0].Data);



    }
}
