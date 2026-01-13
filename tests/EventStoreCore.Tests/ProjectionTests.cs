using EventStoreCore.Abstractions;
using EventStoreCore.MassTransit;
using EventStoreCore;

using EventStoreCore.Postgres;

using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EventStoreCore.Tests.EventStoreFixture;

namespace EventStoreCore.Tests;

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
        public static Task Evolve(UserSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
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

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct)
        {
            return context.DbContext.Set<UserSnapshot>().ExecuteDeleteAsync(ct);
        }
    }


    public class BookEvent
    {
        public int Page { get; set; }
    }

    public class BookPageSummary
    {
        public string Id { get; set; } = string.Empty;
        public Guid BookId { get; set; }
    }

    public class BookProjection : IProjection<BookPageSummary>
    {
        public static Task Evolve(BookPageSummary snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
        {
            switch (@event)
            {
                case IEvent<BookEvent> e:
                    snapshot.Id = $"{e.StreamId}-{e.Data.Page}";
                    snapshot.BookId = e.StreamId;
                    break;
            }
            return Task.FromResult(0);
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct)
        {
            var db = (DbContext)context.ProviderState!;
            return db.Set<BookPageSummary>().ExecuteDeleteAsync(ct);
        }
    }


    [Fact]
    public async Task Projection()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddProjection<EventStoreDbContext, UserProjection, UserSnapshot>(ProjectionMode.Inline, p =>
            {
                p.Handles<UserCreated>();
                p.Handles<UserNameUpdated>();
            });
        });


        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new UserCreated { Name = "John Doe" }, new UserNameUpdated { NewName = "Mary Jane" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshot = await db.Set<UserSnapshot>().FirstOrDefaultAsync(x => x.UserId == streamId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("Mary Jane", snapshot.Name);
    }

    [Fact]
    public async Task ProjectionWithCompositeKey()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddProjection<EventStoreDbContext, BookProjection, BookPageSummary>(ProjectionMode.Inline, p =>
            {
                p.Handles<BookEvent>(e => $"{e.StreamId}-{e.Data.Page}");
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new BookEvent { Page = 1 }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshot = await db.Set<BookPageSummary>().FirstOrDefaultAsync(x => x.Id == $"{streamId}-1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(streamId, snapshot.BookId);
    }

    [Fact]
    public async Task EventualProjectionProcessesViaDaemon()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddSubscriptionDaemon<EventStoreDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));
            c.AddProjection<EventStoreDbContext, UserProjection, UserSnapshot>(ProjectionMode.Eventual, p =>
            {
                p.Handles<UserCreated>();
                p.Handles<UserNameUpdated>();
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new UserCreated { Name = "John Doe" }, new UserNameUpdated { NewName = "Mary Jane" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var daemon = provider.GetRequiredService<SubscriptionDaemon<EventStoreDbContext>>();
        var subscription = provider.GetServices<ISubscription>()
            .OfType<EventualProjectionSubscription<EventStoreDbContext, UserProjection, UserSnapshot>>()
            .Single();

        // Process all pending events (other tests may have added events before us)
        // Keep processing until no more events are available
        while (await daemon.ProcessNextEventAsync(provider.CreateScope(), subscription, TestContext.Current.CancellationToken))
        {
            // Continue processing
        }

        var snapshot = await db.Set<UserSnapshot>().FirstOrDefaultAsync(x => x.UserId == streamId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("Mary Jane", snapshot.Name);
    }
}
