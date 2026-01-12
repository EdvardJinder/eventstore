using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventStoreCore.Tests;


public class ProjectionDaemonDependencyInjectionTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseEventStore();
        }
    }

    private sealed class InlineEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class InlineSnapshot
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class InlineProjection : IProjection<InlineSnapshot>
    {
        public static Task Evolve(InlineSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
        {
            if (@event is IEvent<InlineEvent> inlineEvent)
            {
                snapshot.Id = inlineEvent.StreamId;
                snapshot.Name = inlineEvent.Data.Name;
            }

            return Task.CompletedTask;
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EventualEvent
    {
        public string Code { get; set; } = string.Empty;
    }

    private sealed class EventualSnapshot
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
    }

    private sealed class EventualProjection : IProjection<EventualSnapshot>
    {
        public static Task Evolve(EventualSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
        {
            if (@event is IEvent<EventualEvent> eventualEvent)
            {
                snapshot.Id = eventualEvent.StreamId;
                snapshot.Code = eventualEvent.Data.Code;
            }

            return Task.CompletedTask;
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void ProjectionDaemon_ResolvesDependencies()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=eventstore;Username=postgres;Password=postgres"));

        services.AddEventStore(c =>
        {
            c.ExistingDbContext<TestDbContext>();
            c.AddProjectionDaemon<TestDbContext>(_ => Substitute.For<IDistributedLockProvider>());
        });

        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var daemon = provider.GetRequiredService<ProjectionDaemon<TestDbContext>>();

        Assert.NotNull(daemon);
    }

    [Fact]
    public void ProjectionDaemon_FindsInlineAndEventualProjections()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=eventstore;Username=postgres;Password=postgres"));

        services.AddEventStore(c =>
        {
            c.ExistingDbContext<TestDbContext>();
            c.AddProjection<TestDbContext, InlineProjection, InlineSnapshot>(ProjectionMode.Inline, options =>
            {
                options.Handles<InlineEvent>();
            });
            c.AddProjection<TestDbContext, EventualProjection, EventualSnapshot>(ProjectionMode.Eventual, options =>
            {
                options.Handles<EventualEvent>();
            });
            c.AddProjectionDaemon<TestDbContext>(_ => Substitute.For<IDistributedLockProvider>());
        });

        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var daemon = provider.GetRequiredService<ProjectionDaemon<TestDbContext>>();
        var projections = daemon.Projections;

        Assert.Equal(2, projections.Count);
        Assert.Contains(projections, projection => projection.ProjectionType == typeof(InlineProjection));
        Assert.Contains(projections, projection => projection.ProjectionType == typeof(EventualProjection));
    }
}
