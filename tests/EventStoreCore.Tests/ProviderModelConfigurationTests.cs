using EventStoreCore;
using Microsoft.EntityFrameworkCore;
using PostgresExtensions = EventStoreCore.Postgres.ModelBuilderExtensions;
using SqlServerExtensions = EventStoreCore.SqlServer.ModelBuilderExtensions;

namespace EventStoreCore.Tests;

public class ProviderModelConfigurationTests
{
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

    private sealed class PostgresOutboxContext(DbContextOptions<PostgresOutboxContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            PostgresExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    private sealed class SqlServerOutboxContext(DbContextOptions<SqlServerOutboxContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqlServerExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    [Fact]
    public void PostgresProvider_ConfiguresJsonb()
    {
        var options = new DbContextOptionsBuilder<PostgresContext>()
            .UseNpgsql("Host=localhost;Database=eventstore;Username=postgres;Password=postgres")
            .Options;

        using var context = new PostgresContext(options);
        var entityType = context.Model.FindEntityType(typeof(DbEvent));
        var property = entityType?.FindProperty(nameof(DbEvent.Data));
        var snapshotEntityType = context.Model.FindEntityType(typeof(DbSnapshot));
        var snapshotProperty = snapshotEntityType?.FindProperty(nameof(DbSnapshot.Data));
        var streamEntityType = context.Model.FindEntityType(typeof(DbStream));
        var lifecycleEntityType = context.Model.FindEntityType(typeof(DbStreamLifecycleEntry));

        Assert.NotNull(property);
        Assert.Equal("jsonb", property!.GetColumnType());
        Assert.NotNull(snapshotProperty);
        Assert.Equal("jsonb", snapshotProperty!.GetColumnType());
        Assert.True(streamEntityType?.FindProperty(nameof(DbStream.CurrentVersion))?.IsConcurrencyToken);
        Assert.True(streamEntityType?.FindProperty(nameof(DbStream.LifecycleState))?.IsConcurrencyToken);
        Assert.NotNull(lifecycleEntityType);
    }

    [Fact]
    public void SqlServerProvider_ConfiguresUnicodeColumn()
    {
        var options = new DbContextOptionsBuilder<SqlServerContext>()
            .UseSqlServer("Server=localhost;Database=eventstore;User Id=sa;Password=Pass@word1;TrustServerCertificate=True;")
            .Options;

        using var context = new SqlServerContext(options);
        var entityType = context.Model.FindEntityType(typeof(DbEvent));
        var property = entityType?.FindProperty(nameof(DbEvent.Data));
        var snapshotEntityType = context.Model.FindEntityType(typeof(DbSnapshot));
        var snapshotProperty = snapshotEntityType?.FindProperty(nameof(DbSnapshot.Data));
        var streamEntityType = context.Model.FindEntityType(typeof(DbStream));
        var lifecycleEntityType = context.Model.FindEntityType(typeof(DbStreamLifecycleEntry));

        Assert.NotNull(property);
        Assert.Equal("nvarchar(max)", property!.GetColumnType());
        Assert.NotNull(snapshotProperty);
        Assert.Equal("nvarchar(max)", snapshotProperty!.GetColumnType());
        Assert.True(streamEntityType?.FindProperty(nameof(DbStream.CurrentVersion))?.IsConcurrencyToken);
        Assert.True(streamEntityType?.FindProperty(nameof(DbStream.LifecycleState))?.IsConcurrencyToken);
        Assert.NotNull(lifecycleEntityType);
    }

    [Fact]
    public void PostgresOutboxProvider_ConfiguresJsonb_without_event_store_entities()
    {
        var options = new DbContextOptionsBuilder<PostgresOutboxContext>()
            .UseNpgsql("Host=localhost;Database=eventstore;Username=postgres;Password=postgres")
            .Options;

        using var context = new PostgresOutboxContext(options);
        var outbox = context.Model.FindEntityType(typeof(DbOutboxMessage));

        Assert.Equal("jsonb", outbox?.FindProperty(nameof(DbOutboxMessage.Data))?.GetColumnType());
        Assert.Equal("jsonb", outbox?.FindProperty(nameof(DbOutboxMessage.SourceEntityKey))?.GetColumnType());
        Assert.Null(context.Model.FindEntityType(typeof(DbEvent)));
    }

    [Fact]
    public void SqlServerOutboxProvider_ConfiguresUnicodeColumns_without_event_store_entities()
    {
        var options = new DbContextOptionsBuilder<SqlServerOutboxContext>()
            .UseSqlServer("Server=localhost;Database=eventstore;User Id=sa;Password=Pass@word1;TrustServerCertificate=True;")
            .Options;

        using var context = new SqlServerOutboxContext(options);
        var outbox = context.Model.FindEntityType(typeof(DbOutboxMessage));

        Assert.Equal("nvarchar(max)", outbox?.FindProperty(nameof(DbOutboxMessage.Data))?.GetColumnType());
        Assert.Equal("nvarchar(max)", outbox?.FindProperty(nameof(DbOutboxMessage.SourceEntityKey))?.GetColumnType());
        Assert.Null(context.Model.FindEntityType(typeof(DbEvent)));
    }
}
