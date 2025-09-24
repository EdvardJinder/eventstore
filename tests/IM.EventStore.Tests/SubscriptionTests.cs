using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IM.EventStore.MassTransit;
using MassTransit.Testing;

namespace IM.EventStore.Tests;
public class SubscriptionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{

    public class TestSub : ISubscription
    {
        public static List<IEvent> HandledEvents = new();
        public Task OnEventAsync(IEvent @event, CancellationToken ct)
        {
            HandledEvents.Add(@event);
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

        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });

        services.AddSubscription<EventStoreFixture.EventStoreDbContext, TestSub>(c =>
        {
            c.Handles<TestEvent>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });

        services.AddLogging();

        var provider = services.BuildServiceProvider();

        var subWrapper = provider.GetService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, TestSub>>();

        Assert.NotNull(subWrapper);

    }


    [Fact]
    public async Task SubscriptionCanAquireLease()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });
        services.AddSubscription<EventStoreFixture.EventStoreDbContext, TestSub>(c =>
        {
            c.Handles<TestEvent>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });
        services.AddLogging();

        var provider = services.BuildServiceProvider();

        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, TestSub>>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Database.EnsureCreated();

        var sub = await subWrapper.EnsureSubscriptionRowAsync(db, CancellationToken.None);
        var acquired = await subWrapper.TryAcquireLeaseAsync(db, sub.Id, "test-node", CancellationToken.None);
        Assert.True(acquired);
    }

    [Fact]
    public async Task SubscriptionCanRenewLease()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });
        services.AddSubscription<EventStoreFixture.EventStoreDbContext, TestSub>(c =>
        {
            c.Handles<TestEvent>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, TestSub>>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Database.EnsureCreated();
        var sub = await subWrapper.EnsureSubscriptionRowAsync(db, CancellationToken.None);

        string nodeId = Guid.NewGuid().ToString();

        var acquired = await subWrapper.TryAcquireLeaseAsync(db, sub.Id, nodeId, CancellationToken.None);
        Assert.True(acquired);
        // Try to acquire again with same node id

        var renewed = await subWrapper.RenewLeaseAsync(db, sub.Id, nodeId, CancellationToken.None);

        Assert.True(renewed);
    }

    [Fact]
    public async Task SubscriptionCannotAquireLeaseIfOwnedByAnotherNode()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });
        services.AddSubscription<EventStoreFixture.EventStoreDbContext, TestSub>(c =>
        {
            c.Handles<TestEvent>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, TestSub>>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Database.EnsureCreated();
        var sub = await subWrapper.EnsureSubscriptionRowAsync(db, CancellationToken.None);
        var acquired = await subWrapper.TryAcquireLeaseAsync(db, sub.Id, Guid.NewGuid().ToString(), CancellationToken.None);
        Assert.True(acquired, "Could not aquire first lease");
        // Try to acquire again with different node id
        var acquiredByAnother = await subWrapper.TryAcquireLeaseAsync(db, sub.Id, Guid.NewGuid().ToString(), CancellationToken.None);
        Assert.False(acquiredByAnother, "Lease was aquired by 2 nodes");
    }

    [Fact]
    public async Task SubscriptionCanReleaseLease()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });
        services.AddSubscription<EventStoreFixture.EventStoreDbContext, TestSub>(c =>
        {
            c.Handles<TestEvent>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, TestSub>>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Database.EnsureCreated();
        var sub = await subWrapper.EnsureSubscriptionRowAsync(db, CancellationToken.None);
        var acquired = await subWrapper.TryAcquireLeaseAsync(db, sub.Id, "test-node", CancellationToken.None);
        Assert.True(acquired);
        // Release the lease
        var released = await subWrapper.ReleaseLeaseAsync(db, sub.Id, "test-node", CancellationToken.None);
        Assert.True(released);
        // Try to acquire again with different node id
        var acquiredByAnother = await subWrapper.TryAcquireLeaseAsync(db, sub.Id, "another-node", CancellationToken.None);
        Assert.True(acquiredByAnother);
    }

    [Fact]
    public async Task SubscriptionProcessesEvents()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });
        services.AddSubscription<EventStoreFixture.EventStoreDbContext, TestSub>(c =>
        {
            c.Handles<TestEvent>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, TestSub>>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Database.EnsureCreated();
        // Start the subscription in background
        var host = provider.GetRequiredService<IHostedService>();
        await host.StartAsync(TestContext.Current.CancellationToken);
        // Append some events
        var eventStore = db.Events;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent { Name = "Event 1" }, new TestEvent { Name = "Event 2" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Wait a bit for the subscription to process
        await Task.Delay(5000, TestContext.Current.CancellationToken);
        // Check that events were handled
        Assert.Equal(2, TestSub.HandledEvents.Count);
        Assert.All(TestSub.HandledEvents, e => Assert.IsType<TestEvent>(e.Data));
        // Stop the subscription
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    public class TestEventConsumer : IConsumer<TestEvent>
    {
        public static List<TestEvent> HandledEvents = new();
        public Task Consume(ConsumeContext<TestEvent> context)
        {
            HandledEvents.Add(context.Message);
            return Task.CompletedTask;
        }
    }

    [Fact] 
    async Task MassTransitSubscriptionPublishesAndCanBeConsumed()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
        });

        services.AddMassTransitEventStoreSubscription<EventStoreFixture.EventStoreDbContext>(c =>
        {
            c.Handle<TestEvent, TestEvent>(e => e.Data);
        });

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<TestEventConsumer>();

            x.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, MassTransitEventStoreSubscription>>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Database.EnsureCreated();
        // Start the subscription in background
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        await subWrapper.StartAsync(TestContext.Current.CancellationToken);

        await Task.Delay(5000, TestContext.Current.CancellationToken);

        // Append some events
        var eventStore = db.Events;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new TestEvent { Name = "Event 1" }, new TestEvent { Name = "Event 2" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Wait a bit for the subscription to process
        await Task.Delay(5000, TestContext.Current.CancellationToken);
        // Check that events were handled
        Assert.Equal(2, TestEventConsumer.HandledEvents.Count);
        Assert.All(TestEventConsumer.HandledEvents, e => Assert.IsType<TestEvent>(e));
        // Stop the subscription
        await subWrapper.StopAsync(TestContext.Current.CancellationToken);



    }
}


public class ProjectionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{

    public class UserCreated
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UserNameUpdated
    {
        public Guid UserId { get; set; }
        public string NewName { get; set; } = string.Empty;
    }

    public class UserSnapshot
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UserProjection : IInlineProjection<UserSnapshot>
    {
        public void Evolve(UserSnapshot snapshot, IEvent @event)
        {
            switch (@event.Data)
            {
                case UserCreated uc:
                    snapshot.UserId = uc.UserId;
                    snapshot.Name = uc.Name;
                    return;
                case UserNameUpdated unu:
                    if (snapshot == null) throw new InvalidOperationException("Snapshot cannot be null when updating name");
                    snapshot.Name = unu.NewName;
                    return;
                default:
                    return;
            }
        }
    }

    public class EventualUserProjection : IProjection<UserSnapshot>
    {
        public Task EvolveAsync(UserSnapshot snapshot, IEvent @event, CancellationToken ct)
        {
            switch (@event.Data)
            {
                case UserCreated uc:
                    snapshot.UserId = uc.UserId;
                    snapshot.Name = uc.Name;
                    return Task.CompletedTask;
                case UserNameUpdated unu:
                    if (snapshot == null) throw new InvalidOperationException("Snapshot cannot be null when updating name");
                    snapshot.Name = unu.NewName;
                    return Task.CompletedTask;
                default:
                    return Task.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task InlineProjectionWorks()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        services.AddInlineProjection<UserSnapshot, UserProjection, EventStoreFixture.EventStoreDbContext>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var eventStore = db.Events;
        var userId = Guid.NewGuid();
        eventStore.StartStream(userId, events: [new UserCreated { UserId = userId, Name = "John Doe" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var snapshot = await db.Set<UserSnapshot>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.Equal("John Doe", snapshot!.Name);
        var stream = await eventStore.FetchForWritingAsync(userId, cancellationToken: TestContext.Current.CancellationToken);
        stream!.Append(new UserNameUpdated { UserId = userId, NewName = "Jane Doe" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        snapshot = await db.Set<UserSnapshot>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.Equal("Jane Doe", snapshot!.Name);
    }

    [Fact]
    public async Task EventualProjectionCatchesUp()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventStoreFixture.EventStoreDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            });
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        services.AddProjection<UserSnapshot, EventualUserProjection, EventStoreFixture.EventStoreDbContext>(c =>
        {
            c.Handles<UserCreated>();
            c.Handles<UserNameUpdated>();
            c.SubscribeFrom(DateTimeOffset.MinValue);
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<EventStoreFixture.EventStoreDbContext>>();
        var db = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

      

        var eventStore = db.Events;
        var userId = Guid.NewGuid();
        eventStore.StartStream(userId, events: [new UserCreated { UserId = userId, Name = "John Doe" }, new UserNameUpdated { UserId = userId, NewName = "Jane Doe" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Start the subscription in background
        var subWrapper = provider.GetRequiredService<SubscriptionWrapper<EventStoreFixture.EventStoreDbContext, Projection<UserSnapshot, EventualUserProjection, EventStoreFixture.EventStoreDbContext>>>();
        await subWrapper.StartAsync(TestContext.Current.CancellationToken);

        // await the projection to catch up
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        var snapshot = await db.Set<UserSnapshot>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.Equal("Jane Doe", snapshot!.Name);

    }
}