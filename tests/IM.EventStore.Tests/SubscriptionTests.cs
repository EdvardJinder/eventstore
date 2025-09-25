using IM.EventStore.MassTransit;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using static IM.EventStore.Tests.SubscriptionTests;

namespace IM.EventStore.Tests;
public class SubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{

    public class TestSub : ISubscription
    {
        public static List<IEvent> HandledEvents = new();

        public Task HandleBatchAsync(IEvent[] events, CancellationToken ct)
        {
            HandledEvents.AddRange(events);
            return Task.FromResult(0);
        }

    }

    public class TestEvent2 { }
    public class TestEvent 
    {
        public string Name { get; set; } = "Default";
    }


    [Fact]
    public void SubscriptionIsAddedToDI()
    {
        var services = new ServiceCollection();

        services.AddEventStore<EventStoreFixture.EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        }).AddSubscription<TestSub>();

        services.AddLogging();

        var provider = services.BuildServiceProvider();

        var sub = provider.GetRequiredService<TestSub>();

        Assert.NotNull(sub);

    }



    [Fact]
    public async Task SubscriptionProcessesEvents()
    {
        var services = new ServiceCollection();
        services.AddEventStore<EventStoreFixture.EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        }).AddSubscription<TestSub>(c =>
        {
            c.Handles<TestEvent>();
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams();
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent { Name = "Event 1" }, new TestEvent { Name = "Event 2" }, new TestEvent2 { }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Check that events were handled
        Assert.Equal(2, TestSub.HandledEvents.Count);
        Assert.All(TestSub.HandledEvents, e => Assert.IsType<TestEvent>(e.Data));
    }

    public class TestEventConsumer(EventStoreFixture.EventStoreDbContext db) : IConsumer<EventContext<TestEvent>>
    {
        public async Task Consume(ConsumeContext<EventContext<TestEvent>> context)
        {
            var eventStore = db.Streams();
            var stream = await eventStore.FetchForWritingAsync(context.Message.StreamId, context.Message.TenantId, context.CancellationToken);
            Assert.NotNull(stream);
        }
    }

    [Fact]
    async Task MassTransitSubscriptionPublishesAndCanBeConsumed()
    {
        var services = new ServiceCollection();
        services.AddEventStore<EventStoreFixture.EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        }).AddMassTransitEventStoreSubscription();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<TestEventConsumer>();

            cfg.AddInMemoryInboxOutbox();

            cfg.AddConfigureEndpointsCallback((context, name, cfg) =>
            {
                cfg.UseInMemoryInboxOutbox(context);
            });

            cfg.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>();

        foreach (var service in hosted)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var testHarness = provider.GetRequiredService<ITestHarness>();
        await testHarness.Start();
        var consumer = testHarness.GetConsumerHarness<TestEventConsumer>();
        var eventStore = db.Streams();
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent { Name = "Event 1" }, new TestEvent { Name = "Event 2" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await consumer.Consumed.Any<EventContext<TestEvent>>(TestContext.Current.CancellationToken));

        await testHarness.Stop(cancellationToken: TestContext.Current.CancellationToken);
    }
}
