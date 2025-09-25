using IM.EventStore.MassTransit;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        }).AddSubscription<TestSub>();

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent { Name = "Event 1" }, new TestEvent { Name = "Event 2" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Check that events were handled
        Assert.Equal(2, TestSub.HandledEvents.Count);
        Assert.All(TestSub.HandledEvents, e => Assert.IsType<TestEvent>(e.Data));
    }

    public class TestEventConsumer : IConsumer<EventContext<TestEvent>>
    {
        public Task Consume(ConsumeContext<EventContext<TestEvent>> context)
        {
            return Task.CompletedTask;
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
            cfg.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var testHarness = provider.GetRequiredService<ITestHarness>();
        await testHarness.Start();
        var consumer = testHarness.GetConsumerHarness<TestEventConsumer>();
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent { Name = "Event 1" }, new TestEvent { Name = "Event 2" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await consumer.Consumed.Any<EventContext<TestEvent>>(TestContext.Current.CancellationToken));

        var consumedEvents = consumer.Consumed.Select<EventContext<TestEvent>>(TestContext.Current.CancellationToken);

        Assert.Equal(2, consumedEvents.Count());

        await testHarness.Stop(cancellationToken: TestContext.Current.CancellationToken);
    }
}


public class ProjectionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{

    public class UserCreated
    {
        public string Name { get; set; } = string.Empty;
    }
    public class UserNameUpdated
    {
        public string NewName { get; set; } = string.Empty;
    }
    public class UserSnapshot
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UserProjection : IProjection<UserSnapshot>
    {
        public Task Evolve(UserSnapshot snapshot, IEvent @event, CancellationToken ct)
        {

            switch (@event)
            {
                case IEvent<UserCreated> e:
                    snapshot.UserId = e.StreamId;
                    snapshot.Name = e.Data.Name;
                    break;
                case IEvent<UserNameUpdated> e:
                    snapshot.Name = e.Data.NewName;
                    break;
            }

            return Task.FromResult(0);
        }
    }

   
    [Fact]
    public async Task InlineProjectionWorks()
    {
        var services = new ServiceCollection();
        services.AddEventStore<EventStoreFixture.EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        }).AddProjection<UserProjection, UserSnapshot>();

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new UserCreated { Name = "John Doe" }, new UserNameUpdated { NewName = "Mary Jane"}]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshot = await db.Set<UserSnapshot>().FirstOrDefaultAsync(x => x.UserId == streamId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("Mary Jane", snapshot.Name);
    }

    
}