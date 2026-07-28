using EventStoreCore.MassTransit;
using EventStoreCore;

using EventStoreCore.Postgres;

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
    public class SingleEventConsumer : IConsumer<TestIntegrationEvent>
    {
        public static List<TestIntegrationEvent> HandledEvents { get; } = new();
        public static Guid? LastMessageId { get; private set; }
        public Task Consume(ConsumeContext<TestIntegrationEvent> context)
        {
            HandledEvents.Add(context.Message);
            LastMessageId = context.MessageId;
            return Task.CompletedTask;
        }
    }

    public class MultiEventConsumer : IConsumer<TestIntegrationEvent>
    {
        public static List<TestIntegrationEvent> HandledEvents { get; } = new();
        public Task Consume(ConsumeContext<TestIntegrationEvent> context)
        {
            HandledEvents.Add(context.Message);
            return Task.CompletedTask;
        }
    }

    public class MultiEventConsumer2 : IConsumer<TestIntegrationEvent2>
    {
        public static List<TestIntegrationEvent2> HandledEvents { get; } = new();
        public Task Consume(ConsumeContext<TestIntegrationEvent2> context)
        {
            HandledEvents.Add(context.Message);
            return Task.CompletedTask;
        }
    }
    public record TestIntegrationEvent(Guid Id);
    public record TestIntegrationEvent2(Guid Id, string Name);
    public class TestEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Test";
    }

    [Fact]
    public async Task should_handle_events()
    {
        // Arrange
        SingleEventConsumer.HandledEvents.Clear();

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
            c.AddConsumer<SingleEventConsumer>();
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
        var writtenSequence = await eventStoreDbContext.Set<DbEvent>()
            .Where(e => e.StreamId == streamId)
            .Select(e => e.Sequence)
            .SingleAsync(TestContext.Current.CancellationToken);
        var writtenEventId = await eventStoreDbContext.Set<DbEvent>()
            .Where(e => e.StreamId == streamId)
            .Select(e => e.EventId)
            .SingleAsync(TestContext.Current.CancellationToken);

        var harness = provider.GetTestHarness();
        await harness.Start();

        var consumer = harness.GetConsumerHarness<SingleEventConsumer>();

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetRequiredService<MassTransitSubscription>();
        
        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        Assert.True(await consumer.Consumed.Any<TestIntegrationEvent>(TestContext.Current.CancellationToken));
      
        var subscriptionName = subscription.GetType().AssemblyQualifiedName!;
        var subscriptionEntity = await eventStoreDbContext.Set<DbSubscription>()
          .FirstOrDefaultAsync(s =>
              s.SubscriptionAssemblyQualifiedName == subscriptionName &&
              s.CheckpointScope == EventStoreCore.Abstractions.CheckpointScope.Global &&
              s.TenantId == Guid.Empty,
              TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptionEntity);
        Assert.Equal(writtenSequence, subscriptionEntity.Sequence);
        Assert.True(processed, "No event was processed");
        Assert.Single(SingleEventConsumer.HandledEvents);
        Assert.Equal(writtenEventId, SingleEventConsumer.LastMessageId);
        Assert.IsType<TestIntegrationEvent>(SingleEventConsumer.HandledEvents[0]);



    }

    [Fact]
    public async Task should_handle_multiple_handlers_for_same_event()
    {
        // Arrange
        MultiEventConsumer.HandledEvents.Clear();
        MultiEventConsumer2.HandledEvents.Clear();

        const string expectedName = "TestName";

        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddSubscriptionDaemon<EventStoreDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));

            c.AddMassTransitEventStoreSubscription(t =>
            {
                // Register multiple handlers for the same event type
                t.AddEvent<TestEvent, TestIntegrationEvent>(e => new TestIntegrationEvent(e.Data.Id));
                t.AddEvent<TestEvent, TestIntegrationEvent2>(e => new TestIntegrationEvent2(e.Data.Id, e.Data.Name));
            });
        });

        services.RemoveAll<IHostedService>();

        services.AddMassTransitTestHarness(c =>
        {
            c.AddConsumer<MultiEventConsumer>();
            c.AddConsumer<MultiEventConsumer2>();
        });

        services.AddLogging();

        var provider = services.BuildServiceProvider(true);

        var scope = provider.CreateScope();

        var eventStoreDbContext = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();

        eventStoreDbContext.Database.EnsureCreated();

        var eventStore = eventStoreDbContext.Streams;
        var streamId = Guid.NewGuid();
        var testEvent = new TestEvent { Id = Guid.NewGuid(), Name = expectedName };
        eventStore.StartStream(streamId, events: [testEvent]);
        await eventStoreDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var harness = provider.GetTestHarness();
        await harness.Start();

        var consumer1 = harness.GetConsumerHarness<MultiEventConsumer>();
        var consumer2 = harness.GetConsumerHarness<MultiEventConsumer2>();

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetRequiredService<MassTransitSubscription>();
        
        var processed = await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken);

        // Assert both consumers received their respective events
        Assert.True(await consumer1.Consumed.Any<TestIntegrationEvent>(TestContext.Current.CancellationToken));
        Assert.True(await consumer2.Consumed.Any<TestIntegrationEvent2>(TestContext.Current.CancellationToken));

        Assert.True(processed, "No event was processed");
        Assert.Single(MultiEventConsumer.HandledEvents);
        Assert.Single(MultiEventConsumer2.HandledEvents);
        Assert.Equal(testEvent.Id, MultiEventConsumer.HandledEvents[0].Id);
        Assert.Equal(testEvent.Id, MultiEventConsumer2.HandledEvents[0].Id);
        Assert.Equal(expectedName, MultiEventConsumer2.HandledEvents[0].Name);
    }
}
