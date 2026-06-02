using EventStoreCore.Abstractions;
using EventStoreCore.Hangfire;
using EventStoreCore.Scheduling;
using Hangfire;
using Hangfire.Common;
using Hangfire.MemoryStorage;
using Hangfire.States;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public sealed class SchedulerRegistrationStorePersistenceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SchedulerApplicationDbContext>(options => options.UseSqlite(_connection));
        services.Configure<SchedulerOptions>(options => options.ClaimTimeout = TimeSpan.FromMinutes(1));
        services.AddSingleton(new SchedulerActionCounter());

        var storage = new MemoryStorage();
        services.AddSingleton<JobStorage>(storage);
        services.AddSingleton<IBackgroundJobClient>(sp => new BackgroundJobClient(sp.GetRequiredService<JobStorage>()));

        services.AddEventStore(builder =>
        {
            builder.ExistingDbContext<SchedulerApplicationDbContext>();
            builder.AddScheduler(s =>
            {
                s.UsingHangfire();
                s.On<OrderPlaced>().Hangfire("payment-timeout", static (e, client, sp, _) =>
                {
                    sp.GetRequiredService<SchedulerActionCounter>().Increment();
                    client.Create(
                        Job.FromExpression(() => HangfireProbe.Run(e.Data.OrderId)),
                        new ScheduledState(TimeSpan.FromMinutes(15)));
                    return ValueTask.CompletedTask;
                });
            });
        });

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_persist_one_application_row_for_replayed_event()
    {
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });
        var subscription = _provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerApplicationDbContext>();
        var rows = await dbContext.Set<DbSchedulerEventApplication>()
            .Where(x => x.ProviderName == HangfireSchedulerExtensions.ProviderName &&
                        x.RegistrationName == "payment-timeout" &&
                        x.EventId == placed.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(rows);
        Assert.Equal(Guid.Empty, rows[0].TenantId);
        Assert.NotEqual(Guid.Empty, rows[0].ClaimId);
        Assert.NotNull(rows[0].CompletedAtUtc);
        Assert.Equal(1, _provider.GetRequiredService<SchedulerActionCounter>().Count);
    }

    [Fact]
    public async Task should_scope_application_rows_by_tenant()
    {
        var eventId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var first = new TestEvent<OrderPlaced>(eventId, new OrderPlaced { OrderId = Guid.NewGuid() }, tenantId: tenantA);
        var second = new TestEvent<OrderPlaced>(eventId, new OrderPlaced { OrderId = Guid.NewGuid() }, tenantId: tenantB);
        var subscription = _provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();

        await subscription.Handle(first, TestContext.Current.CancellationToken);
        await subscription.Handle(second, TestContext.Current.CancellationToken);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerApplicationDbContext>();
        var rows = await dbContext.Set<DbSchedulerEventApplication>()
            .Where(x => x.ProviderName == HangfireSchedulerExtensions.ProviderName &&
                        x.EventId == eventId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.TenantId == tenantA);
        Assert.Contains(rows, x => x.TenantId == tenantB);
    }

    [Fact]
    public async Task should_not_apply_fresh_incomplete_claim()
    {
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });
        await InsertApplicationAsync(placed.Id, Guid.Empty, Guid.NewGuid(), DateTime.UtcNow, completedAtUtc: null);
        var subscription = _provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();

        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerApplicationDbContext>();
        var row = await dbContext.Set<DbSchedulerEventApplication>()
            .SingleAsync(x => x.EventId == placed.Id, TestContext.Current.CancellationToken);

        Assert.Null(row.CompletedAtUtc);
        Assert.Equal(0, _provider.GetRequiredService<SchedulerActionCounter>().Count);
    }

    [Fact]
    public async Task should_recover_stale_incomplete_claim()
    {
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });
        var oldClaimId = Guid.NewGuid();
        await InsertApplicationAsync(placed.Id, Guid.Empty, oldClaimId, DateTime.UtcNow.AddMinutes(-10), completedAtUtc: null);
        var subscription = _provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();

        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerApplicationDbContext>();
        var row = await dbContext.Set<DbSchedulerEventApplication>()
            .SingleAsync(x => x.EventId == placed.Id, TestContext.Current.CancellationToken);

        Assert.NotEqual(oldClaimId, row.ClaimId);
        Assert.NotNull(row.CompletedAtUtc);
        Assert.Equal(1, _provider.GetRequiredService<SchedulerActionCounter>().Count);
    }

    private async Task InsertApplicationAsync(
        Guid eventId,
        Guid tenantId,
        Guid claimId,
        DateTime createdAtUtc,
        DateTime? completedAtUtc)
    {
        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulerApplicationDbContext>();
        dbContext.Add(new DbSchedulerEventApplication
        {
            ProviderName = HangfireSchedulerExtensions.ProviderName,
            RegistrationName = "payment-timeout",
            TenantId = tenantId,
            EventId = eventId,
            ClaimId = claimId,
            CreatedAtUtc = createdAtUtc,
            CompletedAtUtc = completedAtUtc
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class SchedulerApplicationDbContext(DbContextOptions<SchedulerApplicationDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            EventStoreCore.ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);
        }
    }

    private sealed class SchedulerActionCounter
    {
        private int _count;

        public int Count => _count;

        public void Increment() => Interlocked.Increment(ref _count);
    }
}
