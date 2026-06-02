using EventStoreCore.Abstractions;
using EventStoreCore.Hangfire;
using Hangfire;
using Hangfire.Common;
using Hangfire.MemoryStorage;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
[Trait("Category", "Containers")]
public sealed class SchedulerEventApplicationPostgresIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PostgresSchedulerDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddSingleton(new SchedulerActionCounter());

        var storage = new MemoryStorage();
        services.AddSingleton<JobStorage>(storage);
        services.AddSingleton<IBackgroundJobClient>(sp => new BackgroundJobClient(sp.GetRequiredService<JobStorage>()));

        services.AddEventStore(builder =>
        {
            builder.ExistingDbContext<PostgresSchedulerDbContext>();
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
        var dbContext = scope.ServiceProvider.GetRequiredService<PostgresSchedulerDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SchedulerEventApplications" (
                "ProviderName" character varying(200) NOT NULL,
                "RegistrationName" character varying(500) NOT NULL,
                "TenantId" uuid NOT NULL,
                "EventId" uuid NOT NULL,
                "ClaimId" uuid NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CompletedAtUtc" timestamp with time zone NULL,
                CONSTRAINT "PK_SchedulerEventApplications" PRIMARY KEY ("ProviderName", "RegistrationName", "TenantId", "EventId")
            );
            """);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_use_postgres_unique_key_for_concurrent_replay_dedupe()
    {
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });
        var subscription = _provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();

        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => subscription.Handle(placed, TestContext.Current.CancellationToken))));

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostgresSchedulerDbContext>();
        var rowCount = await dbContext.Set<DbSchedulerEventApplication>()
            .CountAsync(x => x.ProviderName == HangfireSchedulerExtensions.ProviderName &&
                             x.RegistrationName == "payment-timeout" &&
                             x.EventId == placed.Id,
                TestContext.Current.CancellationToken);

        Assert.Equal(1, rowCount);
        Assert.Equal(1, _provider.GetRequiredService<SchedulerActionCounter>().Count);
    }

    private sealed class PostgresSchedulerDbContext(DbContextOptions<PostgresSchedulerDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            EventStoreCore.Postgres.ModelBuilderExtensions.UseEventStore(modelBuilder);
        }
    }

    private sealed class SchedulerActionCounter
    {
        private int _count;

        public int Count => _count;

        public void Increment() => Interlocked.Increment(ref _count);
    }
}
