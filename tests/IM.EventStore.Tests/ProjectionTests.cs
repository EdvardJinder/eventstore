using IM.EventStore.MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.Tests;

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
        public static Task Evolve(UserSnapshot snapshot, IEvent @event, DbContext db, CancellationToken ct)
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
        public static Task Evolve(BookPageSummary snapshot, IEvent @event, DbContext db, CancellationToken ct)
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
    }


    [Fact]
    public async Task Projection()
    {
        var services = new ServiceCollection();
        services.AddEventStore<EventStoreFixture.EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString, c =>
            {
                c.EnableRetryOnFailure();
            });
        }, c =>
        {
            c.AddProjection<UserProjection, UserSnapshot>(c =>
            {
                c.Handles<UserCreated>();
                c.Handles<UserNameUpdated>();
            });
        });


        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams();
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
        services.AddEventStore<EventStoreFixture.EventStoreDbContext>((sp, options) =>
        {
            options.UseNpgsql(fixture.ConnectionString, c =>
            {
                c.EnableRetryOnFailure();
            });
        }, c =>
        {
            c.AddProjection<BookProjection, BookPageSummary>(c =>
             {
                 c.Handles<BookEvent>(e => $"{e.StreamId}-{e.Data.Page}");
             });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var db = provider.CreateScope().ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
        db.Database.EnsureCreated();
        var eventStore = db.Streams();
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new BookEvent { Page = 1 }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshot = await db.Set<BookPageSummary>().FirstOrDefaultAsync(x => x.Id == $"{streamId}-1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(streamId, snapshot.BookId);
    }
}