using EventStoreCore.Abstractions;
using EventStoreCore.MassTransit;
using EventStoreCore;

using EventStoreCore.Postgres;

using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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

    public class FailingUserSnapshot
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class FailingUserProjection : IProjection<FailingUserSnapshot>
    {
        public static Task Evolve(FailingUserSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
        {
            if (@event is IEvent<UserCreated> e)
            {
                snapshot.UserId = e.StreamId;
                snapshot.Name = e.Data.Name;
            }

            throw new InvalidOperationException("Inline projection failure");
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct)
        {
            return context.DbContext.Set<FailingUserSnapshot>().ExecuteDeleteAsync(ct);
        }
    }

    public class TenantScopedUserSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class TenantScopedUserProjection : IProjection<TenantScopedUserSnapshot>
    {
        public static Task Evolve(TenantScopedUserSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
        {
            switch (@event)
            {
                case IEvent<UserCreated> e:
                    snapshot.Id = $"{e.TenantId}:{e.StreamId}";
                    snapshot.UserId = e.StreamId;
                    snapshot.TenantId = e.TenantId;
                    snapshot.Name = e.Data.Name;
                    break;
                case IEvent<UserNameUpdated> e:
                    snapshot.Id = $"{e.TenantId}:{e.StreamId}";
                    snapshot.UserId = e.StreamId;
                    snapshot.TenantId = e.TenantId;
                    snapshot.Name = e.Data.NewName;
                    break;
            }

            return Task.CompletedTask;
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct)
        {
            return context.DbContext.Set<TenantScopedUserSnapshot>().ExecuteDeleteAsync(ct);
        }
    }

    public class InlineFailureProjectionDbContext : DbContext
    {
        public InlineFailureProjectionDbContext(DbContextOptions<InlineFailureProjectionDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseEventStore();
            modelBuilder.Entity<DbStream>().ToTable("InlineFailureProjectionStreams");
            modelBuilder.Entity<DbEvent>().ToTable("InlineFailureProjectionEvents");
            modelBuilder.Entity<DbProjectionStatus>().ToTable("InlineFailureProjectionStatuses");
            modelBuilder.Entity<DbSubscription>().ToTable("InlineFailureProjectionSubscriptions");
            modelBuilder.Entity<FailingUserSnapshot>(entity =>
            {
                entity.ToTable("InlineFailureProjectionSnapshots");
                entity.HasKey(e => e.UserId);
            });
        }
    }

    public class TenantAwareProjectionDbContext : DbContext
    {
        public TenantAwareProjectionDbContext(DbContextOptions<TenantAwareProjectionDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DbStream>(entity =>
            {
                entity.ToTable("TenantAwareProjectionStreams");
                entity.HasKey(e => new { e.Id, e.TenantId });
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.CurrentVersion);
                entity.Property(e => e.CreatedTimestamp).IsRequired();
                entity.Property(e => e.UpdatedTimestamp).IsRequired();

                entity.HasMany(e => e.Events)
                    .WithOne()
                    .HasForeignKey(e => new { e.StreamId, e.TenantId })
                    .HasPrincipalKey(e => new { e.Id, e.TenantId })
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DbEvent>(entity =>
            {
                entity.ToTable("TenantAwareProjectionEvents");
                entity.HasKey(e => new { e.StreamId, e.TenantId, e.Version });
                entity.HasAlternateKey(e => e.EventId);
                entity.Property(e => e.StreamId).IsRequired();
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.Sequence).ValueGeneratedOnAdd();
                entity.Property(e => e.Version).IsRequired();
                entity.Property(e => e.Type).IsRequired();
                entity.Property(e => e.TypeName).IsRequired().HasDefaultValue(string.Empty);
                entity.Property(e => e.Data).IsRequired().HasColumnType("jsonb");
                entity.Property(e => e.Timestamp).IsRequired();
            });

            modelBuilder.Entity<DbProjectionStatus>(entity =>
            {
                entity.ToTable("TenantAwareProjectionStatuses");
                entity.HasKey(e => e.ProjectionName);
                entity.Property(e => e.ProjectionName).HasMaxLength(500).IsRequired();
                entity.Property(e => e.Version).IsRequired();
                entity.Property(e => e.State).IsRequired();
                entity.Property(e => e.Position).IsRequired();
            });

            modelBuilder.Entity<TenantScopedUserSnapshot>(entity =>
            {
                entity.ToTable("TenantAwareProjectionSnapshots");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.Name).IsRequired();
            });
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
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new UserCreated { Name = "John Doe" }, new UserNameUpdated { NewName = "Mary Jane" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshot = await db.Set<UserSnapshot>().FirstOrDefaultAsync(x => x.UserId == streamId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("Mary Jane", snapshot.Name);
    }

    public sealed class StreamTypeSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public int ApplyCount { get; set; }
    }

    public sealed class StreamTypeProjection : IProjection<StreamTypeSnapshot>
    {
        public static Task Evolve(
            StreamTypeSnapshot snapshot,
            IEvent @event,
            IProjectionContext context,
            CancellationToken ct)
        {
            var typed = (IEvent<UserCreated>)@event;
            snapshot.Id = typed.Data.Name;
            snapshot.ApplyCount++;
            return Task.CompletedTask;
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct) =>
            context.DbContext.Set<StreamTypeSnapshot>().ExecuteDeleteAsync(ct);
    }

    public sealed class StreamTypeProjectionDbContext(
        DbContextOptions<StreamTypeProjectionDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseEventStore();
            modelBuilder.Entity<StreamTypeSnapshot>().HasKey(x => x.Id);
        }
    }

    [Fact]
    public void InlineProjectionRunsForSynchronousSaveChanges()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreDbContext>();
            c.AddProjection<EventStoreDbContext, UserProjection, UserSnapshot>(
                ProjectionMode.Inline,
                p => p.Handles<UserCreated>());
        });
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        var streamId = Guid.NewGuid();
        db.Streams.StartStream(streamId, events: [new UserCreated { Name = "sync" }]);
        db.SaveChanges();

        var snapshot = db.Set<UserSnapshot>().Single(x => x.UserId == streamId);
        Assert.Equal("sync", snapshot.Name);
        Assert.NotNull(db.Set<DbProjectionStatus>()
            .SingleOrDefault(x => x.ProjectionName == typeof(UserProjection).FullName));
    }

    [Fact]
    public async Task InlineProjectionUpdatesStatus()
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
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(UserProjection).FullName!;
        await db.Set<DbProjectionStatus>()
            .Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new UserCreated { Name = "John Doe" }, new UserNameUpdated { NewName = "Mary Jane" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var maxSequence = await db.Set<DbEvent>()
            .Where(e => e.StreamId == streamId)
            .MaxAsync(e => e.Sequence, TestContext.Current.CancellationToken);

        var status = await db.Set<DbProjectionStatus>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(maxSequence, status.Position);
        Assert.Equal(ProjectionState.Active, status.State);
        Assert.NotNull(status.LastProcessedAt);
        Assert.Equal(1, status.Version);
        Assert.Null(status.LastError);
        Assert.Null(status.FailedEventSequence);
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
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        eventStore.StartStream(streamId, events: [new BookEvent { Page = 1 }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshot = await db.Set<BookPageSummary>().FirstOrDefaultAsync(x => x.Id == $"{streamId}-1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(streamId, snapshot.BookId);
    }

    [Fact]
    public async Task InlineProjectionFailureRollsBackAppendSnapshotAndStatus()
    {
        var services = new ServiceCollection();
        services.AddDbContext<InlineFailureProjectionDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<InlineFailureProjectionDbContext>();
            c.AddProjection<InlineFailureProjectionDbContext, FailingUserProjection, FailingUserSnapshot>(ProjectionMode.Inline, p =>
            {
                p.Handles<UserCreated>();
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var streamId = Guid.NewGuid();

        await using (var arrangeScope = provider.CreateAsyncScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<InlineFailureProjectionDbContext>();
            await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await using (var saveScope = provider.CreateAsyncScope())
        {
            var db = saveScope.ServiceProvider.GetRequiredService<InlineFailureProjectionDbContext>();
            db.Streams.StartStream(streamId, events: [new UserCreated { Name = "John Doe" }]);

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var assertScope = provider.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InlineFailureProjectionDbContext>();
        var projectionName = typeof(FailingUserProjection).FullName!;

        var persistedEvents = await assertDb.Set<DbEvent>()
            .Where(e => e.StreamId == streamId)
            .CountAsync(TestContext.Current.CancellationToken);
        var snapshots = await assertDb.Set<FailingUserSnapshot>()
            .Where(e => e.UserId == streamId)
            .CountAsync(TestContext.Current.CancellationToken);
        var status = await assertDb.Set<DbProjectionStatus>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectionName == projectionName, TestContext.Current.CancellationToken);

        Assert.Equal(0, persistedEvents);
        Assert.Equal(0, snapshots);
        Assert.Null(status);
    }

    [Fact]
    public async Task InlineProjectionMatchesTenantWhenStreamIdsOverlap()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TenantAwareProjectionDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<TenantAwareProjectionDbContext>();
            c.AddProjection<TenantAwareProjectionDbContext, TenantScopedUserProjection, TenantScopedUserSnapshot>(ProjectionMode.Inline, p =>
            {
                p.Handles<UserCreated>(e => $"{e.TenantId}:{e.StreamId}");
                p.Handles<UserNameUpdated>(e => $"{e.TenantId}:{e.StreamId}");
            });
        });

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantAwareProjectionDbContext>();
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var eventStore = db.Streams;
        var streamId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        eventStore.StartStream(streamId, tenantA, events: [new UserCreated { Name = "Tenant A" }]);
        eventStore.StartStream(streamId, tenantB, events: [new UserCreated { Name = "Tenant B" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshotA = await db.Set<TenantScopedUserSnapshot>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == $"{tenantA}:{streamId}", TestContext.Current.CancellationToken);
        var snapshotB = await db.Set<TenantScopedUserSnapshot>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == $"{tenantB}:{streamId}", TestContext.Current.CancellationToken);

        Assert.Equal("Tenant A", snapshotA.Name);
        Assert.Equal(tenantA, snapshotA.TenantId);
        Assert.Equal("Tenant B", snapshotB.Name);
        Assert.Equal(tenantB, snapshotB.TenantId);
    }

    [Fact]
    public async Task InlineProjectionMatchesStreamTypeWhenStreamIdsOverlap()
    {
        var services = new ServiceCollection();
        services.AddDbContext<StreamTypeProjectionDbContext>(
            options => options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<StreamTypeProjectionDbContext>();
            c.AddProjection<StreamTypeProjectionDbContext, StreamTypeProjection, StreamTypeSnapshot>(
                ProjectionMode.Inline,
                p => p.Handles<UserCreated>(e => e.Data.Name));
        });
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StreamTypeProjectionDbContext>();
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        db.Streams.StartStream("orders", streamId, events: [new UserCreated { Name = "orders" }]);
        db.Streams.StartStream("audit", streamId, events: [new UserCreated { Name = "audit" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var snapshots = await db.Set<StreamTypeSnapshot>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal(1, snapshot.ApplyCount));
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
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
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
