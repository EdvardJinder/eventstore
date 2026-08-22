using EventStoreCore.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public sealed class InlineEventHandlerTests
{
    [Fact]
    public async Task Stream_handlers_share_the_committing_scope_and_honor_order_and_source_filters()
    {
        await using var fixture = await InlineFixture.CreateAsync(handlers =>
        {
            handlers.Add<SecondStreamHandler, SharedEvent>(options => options.Order = 20);
            handlers.Add<FirstStreamHandler, SharedEvent>(options =>
            {
                options.Order = 10;
                options.Sources = InlineEventSource.Stream;
            });
            handlers.Add<EntityOnlySharedHandler, SharedEvent>(options =>
                options.Sources = InlineEventSource.EntityOutbox);
            handlers.Add<ThirdStreamHandler, SharedEvent>(options => options.Order = 20);
        });
        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
        var eventId = Guid.NewGuid();

        db.Streams.StartStream(Guid.NewGuid(), new SharedEvent("stream").WithEventId(eventId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var recorder = scope.ServiceProvider.GetRequiredService<Recorder>();
        Assert.Equal(["first", "second", "third"], recorder.Entries);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<EntityOnlySharedHandler>().Calls);
        var first = scope.ServiceProvider.GetRequiredService<FirstStreamHandler>();
        Assert.Same(db, first.Context);
        Assert.True(first.SawTypedStreamEnvelope);
        Assert.Equal(eventId, first.EventId);
        Assert.Single(await db.Reactions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entity_handlers_dispatch_cascades_breadth_first_and_keep_every_outbox_event()
    {
        await using var fixture = await InlineFixture.CreateAsync(handlers =>
        {
            handlers.Add<SecondOrderHandler, OrderAdded>(options =>
            {
                options.Order = 20;
                options.Sources = InlineEventSource.EntityOutbox;
            });
            handlers.Add<FirstOrderHandler, OrderAdded>(options =>
            {
                options.Order = 10;
                options.Sources = InlineEventSource.EntityOutbox;
            });
            handlers.Add<ShipmentHandler, ShipmentAdded>(options =>
                options.Sources = InlineEventSource.EntityOutbox);
        });
        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
        var orderId = Guid.NewGuid();

        db.Orders.Add(new Order { Id = orderId, Status = "new" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var recorder = scope.ServiceProvider.GetRequiredService<Recorder>();
        Assert.Equal(["order:first", "order:second", "shipment"], recorder.Entries);
        Assert.True(scope.ServiceProvider.GetRequiredService<FirstOrderHandler>().SawTypedOutboxEnvelope);
        Assert.Single(await db.Shipments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            await db.Set<DbOutboxMessage>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entity_capture_observes_mutations_from_an_earlier_entity_handler()
    {
        await using var fixture = await InlineFixture.CreateAsync(handlers =>
        {
            handlers.Add<MutatePendingShipmentHandler, OrderAdded>(options =>
                options.Sources = InlineEventSource.EntityOutbox);
            handlers.Add<PendingShipmentHandler, ShipmentAdded>(options =>
                options.Sources = InlineEventSource.EntityOutbox);
        });
        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        db.Orders.Add(new Order { Id = orderId, Status = "new" });
        db.Shipments.Add(new Shipment
        {
            Id = shipmentId,
            OrderId = orderId,
            Status = "pending"
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var shipmentHandler = scope.ServiceProvider.GetRequiredService<PendingShipmentHandler>();
        Assert.Equal("ready", shipmentHandler.Status);
        var reader = scope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var events = await reader.ReadAsync(0, ct: TestContext.Current.CancellationToken);
        var shipmentEvent = Assert.IsAssignableFrom<IOutboxEvent<ShipmentAdded>>(
            events.Single(@event => @event.EventType == typeof(ShipmentAdded)));
        Assert.Equal("ready", shipmentEvent.Data.Status);
    }

    [Fact]
    public void Pre_registered_inline_handlers_must_have_scoped_lifetime()
    {
        var singletonServices = new ServiceCollection();
        singletonServices.AddSingleton<CountingHandler>();
        var singletonException = Assert.Throws<InvalidOperationException>(() =>
            singletonServices.AddInlineEventHandlers<InlineDbContext>(handlers =>
                handlers.Add<CountingHandler, SharedEvent>()));
        Assert.Contains("scoped lifetime", singletonException.Message, StringComparison.Ordinal);

        var transientServices = new ServiceCollection();
        transientServices.AddTransient<CountingHandler>();
        var transientException = Assert.Throws<InvalidOperationException>(() =>
            transientServices.AddInlineEventHandlers<InlineDbContext>(handlers =>
                handlers.Add<CountingHandler, SharedEvent>()));
        Assert.Contains("scoped lifetime", transientException.Message, StringComparison.Ordinal);

        var scopedServices = new ServiceCollection();
        scopedServices.AddScoped<CountingHandler>();
        scopedServices.AddInlineEventHandlers<InlineDbContext>(handlers =>
            handlers.Add<CountingHandler, SharedEvent>());
    }

    [Fact]
    public async Task Handler_failure_rolls_back_originating_event_and_tracked_side_effects()
    {
        await using var fixture = await InlineFixture.CreateAsync(handlers =>
            handlers.Add<FailingHandler, SharedEvent>());
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
            db.Streams.StartStream(Guid.NewGuid(), new SharedEvent("fail"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

            Assert.Equal("handler failed", exception.Message);
        }

        await using var assertionScope = fixture.Provider.CreateAsyncScope();
        var assertDb = assertionScope.ServiceProvider.GetRequiredService<InlineDbContext>();
        Assert.Empty(await assertDb.Set<DbStream>().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await assertDb.Reactions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Nested_save_and_stream_append_are_rejected()
    {
        await using (var nestedFixture = await InlineFixture.CreateAsync(handlers =>
            handlers.Add<NestedSaveHandler, SharedEvent>()))
        {
            await using var scope = nestedFixture.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
            db.Streams.StartStream(Guid.NewGuid(), new SharedEvent("nested"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

            Assert.Contains("cannot call SaveChanges", exception.Message, StringComparison.Ordinal);
        }

        await using var appendFixture = await InlineFixture.CreateAsync(handlers =>
            handlers.Add<StreamAppendingHandler, SharedEvent>());
        await using var appendScope = appendFixture.Provider.CreateAsyncScope();
        var appendDb = appendScope.ServiceProvider.GetRequiredService<InlineDbContext>();
        appendDb.Streams.StartStream(Guid.NewGuid(), new SharedEvent("append"));

        var appendException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => appendDb.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("cannot append stream events", appendException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_limit_aborts_an_entity_cascade()
    {
        await using var fixture = await InlineFixture.CreateAsync(handlers =>
        {
            handlers.MaxDispatchCount = 1;
            handlers.Add<FirstOrderHandler, OrderAdded>(options =>
                options.Sources = InlineEventSource.EntityOutbox);
            handlers.Add<ShipmentHandler, ShipmentAdded>(options =>
                options.Sources = InlineEventSource.EntityOutbox);
        });
        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
        db.Orders.Add(new Order { Id = Guid.NewGuid(), Status = "new" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("configured limit of 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Synchronous_save_is_supported_and_committed_append_retries_do_not_redispatch()
    {
        await using var fixture = await InlineFixture.CreateAsync(handlers =>
            handlers.Add<CountingHandler, SharedEvent>(options =>
                options.Sources = InlineEventSource.Stream));
        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
        var counter = scope.ServiceProvider.GetRequiredService<CountingHandler>();

        db.Streams.StartStream(Guid.NewGuid(), new SharedEvent("sync"));
        db.SaveChanges();
        Assert.Equal(1, counter.Calls);

        var streamId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var operation = new AppendOperation(
            streamId,
            ExpectedVersion.NoStream,
            [new SharedEvent("retry").WithEventId(eventId)]);

        var first = await db.Streams.AppendAsync(operation, TestContext.Current.CancellationToken);
        var retry = await db.Streams.AppendAsync(operation, TestContext.Current.CancellationToken);

        Assert.False(first.WasAlreadyCommitted);
        Assert.True(retry.WasAlreadyCommitted);
        Assert.Equal(2, counter.Calls);
    }

    private sealed class FirstStreamHandler(InlineDbContext dbContext, Recorder recorder)
        : IInlineEventHandler<SharedEvent>
    {
        public InlineDbContext Context { get; } = dbContext;

        public bool SawTypedStreamEnvelope { get; private set; }

        public Guid EventId { get; private set; }

        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            SawTypedStreamEnvelope = @event is IEvent<SharedEvent> { Sequence: 0 };
            EventId = @event.Id;
            recorder.Entries.Add("first");
            Context.Reactions.Add(new Reaction { Id = Guid.NewGuid(), Value = @event.Data.Value });
            return Task.CompletedTask;
        }
    }

    private sealed class SecondStreamHandler(Recorder recorder) : IInlineEventHandler<SharedEvent>
    {
        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            recorder.Entries.Add("second");
            return Task.CompletedTask;
        }
    }

    private sealed class ThirdStreamHandler(Recorder recorder) : IInlineEventHandler<SharedEvent>
    {
        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            recorder.Entries.Add("third");
            return Task.CompletedTask;
        }
    }

    private sealed class EntityOnlySharedHandler : IInlineEventHandler<SharedEvent>
    {
        public int Calls { get; private set; }

        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FirstOrderHandler(InlineDbContext context, Recorder recorder)
        : IInlineEventHandler<OrderAdded>
    {
        public bool SawTypedOutboxEnvelope { get; private set; }

        public Task Handle(IEventEnvelope<OrderAdded> @event, CancellationToken ct)
        {
            SawTypedOutboxEnvelope = @event is IOutboxEvent<OrderAdded> { Sequence: 0 };
            recorder.Entries.Add("order:first");
            context.Shipments.Add(new Shipment { Id = Guid.NewGuid(), OrderId = @event.Data.OrderId });
            return Task.CompletedTask;
        }
    }

    private sealed class SecondOrderHandler(Recorder recorder) : IInlineEventHandler<OrderAdded>
    {
        public Task Handle(IEventEnvelope<OrderAdded> @event, CancellationToken ct)
        {
            recorder.Entries.Add("order:second");
            return Task.CompletedTask;
        }
    }

    private sealed class ShipmentHandler(Recorder recorder) : IInlineEventHandler<ShipmentAdded>
    {
        public Task Handle(IEventEnvelope<ShipmentAdded> @event, CancellationToken ct)
        {
            recorder.Entries.Add("shipment");
            return Task.CompletedTask;
        }
    }

    private sealed class MutatePendingShipmentHandler(InlineDbContext context)
        : IInlineEventHandler<OrderAdded>
    {
        public Task Handle(IEventEnvelope<OrderAdded> @event, CancellationToken ct)
        {
            context.Shipments.Local.Single(shipment => shipment.OrderId == @event.Data.OrderId).Status = "ready";
            return Task.CompletedTask;
        }
    }

    private sealed class PendingShipmentHandler : IInlineEventHandler<ShipmentAdded>
    {
        public string? Status { get; private set; }

        public Task Handle(IEventEnvelope<ShipmentAdded> @event, CancellationToken ct)
        {
            Status = @event.Data.Status;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler(InlineDbContext context) : IInlineEventHandler<SharedEvent>
    {
        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            context.Reactions.Add(new Reaction { Id = Guid.NewGuid(), Value = "not committed" });
            throw new InvalidOperationException("handler failed");
        }
    }

    private sealed class NestedSaveHandler(InlineDbContext context) : IInlineEventHandler<SharedEvent>
    {
        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct) =>
            context.SaveChangesAsync(ct);
    }

    private sealed class StreamAppendingHandler(InlineDbContext context) : IInlineEventHandler<SharedEvent>
    {
        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            context.Streams.StartStream(Guid.NewGuid(), new DerivedEvent());
            return Task.CompletedTask;
        }
    }

    private sealed class CountingHandler : IInlineEventHandler<SharedEvent>
    {
        public int Calls { get; private set; }

        public Task Handle(IEventEnvelope<SharedEvent> @event, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class Recorder
    {
        public List<string> Entries { get; } = [];
    }

    private sealed record SharedEvent(string Value);

    private sealed record DerivedEvent;

    private sealed record OrderAdded(Guid OrderId);

    private sealed record ShipmentAdded(Guid ShipmentId, Guid OrderId, string Status);

    private sealed class Order
    {
        public Guid Id { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    private sealed class Shipment
    {
        public Guid Id { get; set; }

        public Guid OrderId { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    private sealed class Reaction
    {
        public Guid Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    private sealed class InlineDbContext(DbContextOptions<InlineDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public DbSet<Shipment> Shipments => Set<Shipment>();

        public DbSet<Reaction> Reactions => Set<Reaction>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            EventStoreCore.Sqlite.ModelBuilderExtensions.UseEventStore(modelBuilder);
            EventStoreCore.Sqlite.ModelBuilderExtensions.UseEntityOutbox(modelBuilder);
            modelBuilder.Entity<Order>().HasKey(order => order.Id);
            modelBuilder.Entity<Shipment>().HasKey(shipment => shipment.Id);
            modelBuilder.Entity<Reaction>().HasKey(reaction => reaction.Id);
        }
    }

    private sealed class InlineFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private InlineFixture(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<InlineFixture> CreateAsync(
            Action<IInlineEventHandlerBuilder> configureHandlers)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var services = new ServiceCollection();
            services.AddDbContext<InlineDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<Recorder>();
            services.AddEventStore(builder => builder.ExistingDbContext<InlineDbContext>());
            services.AddEntityOutbox<InlineDbContext>(outbox =>
            {
                outbox.For<Order>().On(change =>
                    change.Added(entity => new OrderAdded(entity.Entity.Id)));
                outbox.For<Shipment>().On(change =>
                    change.Added(entity => new ShipmentAdded(
                        entity.Entity.Id,
                        entity.Entity.OrderId,
                        entity.Entity.Status)));
            });
            services.AddInlineEventHandlers<InlineDbContext>(configureHandlers);

            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<InlineDbContext>();
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            }

            return new InlineFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
