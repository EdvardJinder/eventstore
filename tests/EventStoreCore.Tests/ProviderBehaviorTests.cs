using EventStoreCore;
using Microsoft.EntityFrameworkCore;
using PostgresExtensions = EventStoreCore.Postgres.ModelBuilderExtensions;
using SqlServerExtensions = EventStoreCore.SqlServer.ModelBuilderExtensions;

namespace EventStoreCore.Tests;

public class ProviderBehaviorTests : IClassFixture<PostgresFixture>, IClassFixture<SqlServerFixture>
{
    private readonly PostgresFixture _postgresFixture;
    private readonly SqlServerFixture _sqlServerFixture;

    public ProviderBehaviorTests(
        PostgresFixture postgresFixture,
        SqlServerFixture sqlServerFixture)
    {
        _postgresFixture = postgresFixture;
        _sqlServerFixture = sqlServerFixture;
    }

    public enum ProviderKind
    {
        Postgres,
        SqlServer
    }

    public static IEnumerable<object[]> Providers =>
    [
        [ProviderKind.Postgres],
        [ProviderKind.SqlServer]
    ];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task EventStore_WritesAndReads(ProviderKind provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var store = new DbContextEventStore(context);
        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        store.StartStream(streamId, tenantId, events: new SampleEvent { Name = "Hello" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stream = await store.FetchForReadingAsync(streamId, tenantId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        Assert.Single(stream!.Events);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    private DbContext CreateContext(ProviderKind provider)
    {
        return provider switch
        {
            ProviderKind.Postgres => new PostgresContext(new DbContextOptionsBuilder<PostgresContext>()
                .UseNpgsql(_postgresFixture.ConnectionString)
                .Options),
            ProviderKind.SqlServer => new SqlServerContext(new DbContextOptionsBuilder<SqlServerContext>()
                .UseSqlServer(_sqlServerFixture.ConnectionString)
                .Options),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

   
    private sealed class PostgresContext : DbContext
    {
        public PostgresContext(DbContextOptions<PostgresContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            PostgresExtensions.UseEventStore(modelBuilder);
        }
    }

    private sealed class SqlServerContext : DbContext
    {
        public SqlServerContext(DbContextOptions<SqlServerContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqlServerExtensions.UseEventStore(modelBuilder);
        }
    }

    
    private sealed class SampleEvent
    {
        public string Name { get; set; } = string.Empty;
    }
}
