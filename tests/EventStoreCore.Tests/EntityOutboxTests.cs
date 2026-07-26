using System.Text.Json;
using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventStoreCore.Tests;

public sealed class EntityOutboxTests
{
    [Fact]
    public async Task Captures_added_modified_and_deleted_events_atomically()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
        {
            outbox.For<Order>()
                .TenantId(order => order.TenantId)
                .On(change => change
                    .Added(entity => [new OrderAdded(entity.Entity.Id, entity.Entity.Status)])
                    .Modified(entity => entity.IsModified(order => order.Status)
                        ? [new OrderModified(
                            entity.Entity.Id,
                            entity.Original(order => order.Status)!,
                            entity.Current(order => order.Status)!)]
                        : [])
                    .Deleted(entity =>
                    [
                        new OrderDeleted(entity.Entity.Id),
                        new OrderAudit(entity.Entity.Id, "deleted")
                    ]));
        });

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.Orders.Add(new Order { Id = orderId, TenantId = tenantId, Status = "new" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var order = await db.Orders.SingleAsync(TestContext.Current.CancellationToken);
            order.Status = "paid";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.Remove(order);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var assertScope = provider.CreateAsyncScope();
        var reader = assertScope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var events = await reader.ReadAsync(0, 10, tenantId, TestContext.Current.CancellationToken);

        Assert.Collection(
            events,
            @event =>
            {
                var typed = Assert.IsAssignableFrom<IOutboxEvent<OrderAdded>>(@event);
                Assert.Equal("new", typed.Data.Status);
                Assert.Equal(EntityChangeKind.Added, typed.ChangeKind);
                Assert.Equal(tenantId, typed.TenantId);
                Assert.Contains(orderId.ToString(), typed.SourceEntityKey, StringComparison.OrdinalIgnoreCase);
            },
            @event =>
            {
                var typed = Assert.IsAssignableFrom<IOutboxEvent<OrderModified>>(@event);
                Assert.Equal("new", typed.Data.OriginalStatus);
                Assert.Equal("paid", typed.Data.CurrentStatus);
                Assert.Equal(EntityChangeKind.Modified, typed.ChangeKind);
            },
            @event => Assert.IsAssignableFrom<IOutboxEvent<OrderDeleted>>(@event),
            @event => Assert.IsAssignableFrom<IOutboxEvent<OrderAudit>>(@event));
    }

    [Fact]
    public async Task A_factory_failure_aborts_the_entity_save()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
        {
            outbox.For<Order>().On(change =>
                change.Added(_ => throw new InvalidOperationException("capture failed")));
        });

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        db.Orders.Add(new Order { Id = Guid.NewGuid(), Status = "new" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("capture failed", exception.Message);
        await using var assertion = provider.CreateAsyncScope();
        var assertDb = assertion.ServiceProvider.GetRequiredService<OutboxDbContext>();
        Assert.Empty(await assertDb.Orders.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await assertDb.Set<DbOutboxMessage>().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_event_array_skips_outbox_capture()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
            outbox.For<Order>().On(change => change.Added(_ => [])));

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        db.Orders.Add(new Order { Id = Guid.NewGuid(), Status = "new" });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await db.Set<DbOutboxMessage>().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Synchronous_save_captures_a_registered_logical_event_name()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
        {
            outbox.AddEvent<OrderAdded>("order_added_v1");
            outbox.For<Order>().On(change =>
                change.Added(entity => [new OrderAdded(entity.Entity.Id, entity.Entity.Status)]));
        });

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        db.Orders.Add(new Order { Id = Guid.NewGuid(), Status = "new" });

        db.SaveChanges();

        var message = db.Set<DbOutboxMessage>().Single();
        Assert.Equal("order_added_v1", message.TypeName);
    }

    [Fact]
    public async Task Reader_applies_event_type_upcasters_registered_through_the_outbox_builder()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
            outbox.AddEvent<OrderAdded>(
                "order_added_v2",
                eventType => eventType.AddUpcaster<LegacyOrderAdded>(
                    "order_added_v1",
                    oldEvent => new OrderAdded(oldEvent.OrderId, oldEvent.State))));

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);
        var orderId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.Add(new DbOutboxMessage
            {
                EventId = Guid.NewGuid(),
                Type = typeof(LegacyOrderAdded).AssemblyQualifiedName!,
                TypeName = "order_added_v1",
                Data = JsonSerializer.Serialize(new LegacyOrderAdded(orderId, "new")),
                Timestamp = DateTimeOffset.UtcNow,
                SourceEntityType = typeof(Order).AssemblyQualifiedName!,
                SourceEntityKey = JsonSerializer.Serialize(new { Id = orderId }),
                ChangeKind = EntityChangeKind.Added
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readScope = provider.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var events = await reader.ReadAsync(0, ct: TestContext.Current.CancellationToken);

        var typed = Assert.IsAssignableFrom<IOutboxEvent<OrderAdded>>(Assert.Single(events));
        Assert.Equal(orderId, typed.Data.Id);
        Assert.Equal("new", typed.Data.Status);
    }

    [Fact]
    public async Task Reader_filters_by_tenant_and_cleanup_stops_at_slowest_checkpoint()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
            outbox.For<Order>().TenantId(order => order.TenantId).On(change =>
                change.Added(entity => [new OrderAdded(entity.Entity.Id, entity.Entity.Status)])));

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.Orders.AddRange(
                new Order { Id = Guid.NewGuid(), TenantId = tenantA, Status = "a" },
                new Order { Id = Guid.NewGuid(), TenantId = tenantB, Status = "b" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var sequences = await db.Set<DbOutboxMessage>()
                .OrderBy(message => message.Sequence)
                .Select(message => message.Sequence)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            db.AddRange(
                NewCheckpoint("fast", sequences[1]),
                NewCheckpoint("slow", sequences[0]));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readerScope = provider.CreateAsyncScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var tenantEvents = await reader.ReadAsync(0, 10, tenantB, TestContext.Current.CancellationToken);
        Assert.Single(tenantEvents);
        Assert.Equal(tenantB, tenantEvents[0].TenantId);

        var deleted = await reader.CleanupAsync(long.MaxValue, TestContext.Current.CancellationToken);
        Assert.Equal(1, deleted);

        var remaining = await reader.ReadAsync(0, 10, null, TestContext.Current.CancellationToken);
        Assert.Single(remaining);
        Assert.Equal(tenantB, remaining[0].TenantId);
    }

    [Fact]
    public async Task Daemon_checkpoints_each_subscription_independently()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddLogging();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
            outbox.For<Order>().On(change =>
                change.Added(entity => [new OrderAdded(entity.Entity.Id, entity.Entity.Status)])));
        fixture.Services.AddOutboxSubscription<RecordingSubscription>();
        fixture.Services.AddEntityOutboxDaemon<OutboxDbContext>(
            _ => Substitute.For<IDistributedLockProvider>(),
            options => options.BatchSize = 10);

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);

        await using (var writeScope = provider.CreateAsyncScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.Orders.Add(new Order { Id = Guid.NewGuid(), Status = "new" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var daemon = provider.GetRequiredService<EntityOutboxDaemon<OutboxDbContext>>();
        var subscription = provider.GetRequiredService<RecordingSubscription>();
        using var processScope = provider.CreateScope();
        var processed = await daemon.ProcessNextBatchAsync(
            processScope,
            subscription,
            CheckpointScopeKey.Global,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, processed);
        Assert.Single(subscription.Events);

        await using var assertScope = provider.CreateAsyncScope();
        var dbContext = assertScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var checkpoint = await dbContext.Set<DbOutboxSubscription>()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(subscription.Events[0].Sequence, checkpoint.Sequence);
        Assert.Equal(SubscriptionState.Active, checkpoint.State);
    }

    [Fact]
    public async Task Failed_subscription_does_not_block_another_subscription()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Services.AddLogging();
        fixture.Services.AddEntityOutbox<OutboxDbContext>(outbox =>
            outbox.For<Order>().On(change =>
                change.Added(entity => [new OrderAdded(entity.Entity.Id, entity.Entity.Status)])));
        fixture.Services.AddOutboxSubscription<RecordingSubscription>();
        fixture.Services.AddOutboxSubscription<FailingSubscription>();
        fixture.Services.AddEntityOutboxDaemon<OutboxDbContext>(
            _ => Substitute.For<IDistributedLockProvider>(),
            options =>
            {
                options.BatchSize = 10;
                options.RetryDelay = TimeSpan.Zero;
            });

        await using var provider = fixture.Build();
        await fixture.CreateSchemaAsync(provider);

        await using (var writeScope = provider.CreateAsyncScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.Orders.Add(new Order { Id = Guid.NewGuid(), Status = "new" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var daemon = provider.GetRequiredService<EntityOutboxDaemon<OutboxDbContext>>();
        using (var failingScope = provider.CreateScope())
        {
            var processed = await daemon.ProcessNextBatchAsync(
                failingScope,
                provider.GetRequiredService<FailingSubscription>(),
                CheckpointScopeKey.Global,
                TestContext.Current.CancellationToken);
            Assert.Equal(0, processed);
        }

        using (var recordingScope = provider.CreateScope())
        {
            var processed = await daemon.ProcessNextBatchAsync(
                recordingScope,
                provider.GetRequiredService<RecordingSubscription>(),
                CheckpointScopeKey.Global,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, processed);
        }

        await using var assertScope = provider.CreateAsyncScope();
        var checkpoints = await assertScope.ServiceProvider.GetRequiredService<OutboxDbContext>()
            .Set<DbOutboxSubscription>()
            .OrderBy(checkpoint => checkpoint.State)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(checkpoints, checkpoint =>
            checkpoint.State == SubscriptionState.Faulted &&
            checkpoint.Sequence == 0 &&
            checkpoint.FailedEventSequence.HasValue);
        Assert.Contains(checkpoints, checkpoint =>
            checkpoint.State == SubscriptionState.Active &&
            checkpoint.Sequence > 0);
    }

    private static DbOutboxSubscription NewCheckpoint(string name, long sequence) => new()
    {
        SubscriptionAssemblyQualifiedName = name,
        Sequence = sequence
    };

    private sealed class RecordingSubscription : IOutboxSubscription
    {
        public List<IOutboxEvent> Events { get; } = [];

        public Task Handle(IOutboxEvent @event, CancellationToken ct)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSubscription : IOutboxSubscription
    {
        public Task Handle(IOutboxEvent @event, CancellationToken ct)
        {
            throw new InvalidOperationException("publisher unavailable");
        }
    }

    private sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ModelBuilderExtensions.ConfigureEntityOutboxModel(modelBuilder);
            modelBuilder.Entity<Order>().HasKey(order => order.Id);
        }
    }

    private sealed class Order
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    private sealed record OrderAdded(Guid Id, string Status);

    private sealed record LegacyOrderAdded(Guid OrderId, string State);

    private sealed record OrderModified(Guid Id, string OriginalStatus, string CurrentStatus);

    private sealed record OrderDeleted(Guid Id);

    private sealed record OrderAudit(Guid Id, string Action);

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private OutboxFixture(SqliteConnection connection)
        {
            _connection = connection;
            Services = new ServiceCollection();
            Services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection));
        }

        public ServiceCollection Services { get; }

        public static async Task<OutboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return new OutboxFixture(connection);
        }

        public ServiceProvider Build() => Services.BuildServiceProvider();

        public async Task CreateSchemaAsync(ServiceProvider provider)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }
}
