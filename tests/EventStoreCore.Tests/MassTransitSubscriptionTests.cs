using EventStoreCore.MassTransit;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading.Postgres;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static EventStoreCore.Tests.EventStoreFixture;

namespace EventStoreCore.Tests;

public class MassTransitSubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public class TestConsumer : IConsumer<TestIntegrationEvent>
    {
        public static List<TestIntegrationEvent> HandledEvents { get; } = new();
        public Task Consume(ConsumeContext<TestIntegrationEvent> context)
        {
            HandledEvents.Add(context.Message);
            return Task.CompletedTask;
        }
    }
    public record TestIntegrationEvent(Guid Id);
    public class TestEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    [Fact]
    public async Task should_handle_events()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddSubscriptionDaemon<EventStoreDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));

                c.AddMassTransitEventStoreSubscription(t =>
                {
                    t.AddEvent<TestEvent, TestIntegrationEvent>(e => new TestIntegrationEvent(e.Data.Id));
                });
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

        var eventStore = eventStoreDbContext.Streams();
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent()]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var harness = provider.GetTestHarness();
        await harness.Start();

        var consumer = harness.GetConsumerHarness<TestConsumer>();

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetRequiredService<MassTransitSubscription>();
        
        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        Assert.True(await consumer.Consumed.Any<TestIntegrationEvent>(TestContext.Current.CancellationToken));
      
        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
          .FindAsync([subscription.GetType().AssemblyQualifiedName], TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(1, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(TestConsumer.HandledEvents);
        Assert.IsType<TestIntegrationEvent>(TestConsumer.HandledEvents[0]);



    }
}
