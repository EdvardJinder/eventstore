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

    [Fact]
    public void PostgresProvider_ConfiguresJsonb()
    {
        var options = new DbContextOptionsBuilder<PostgresContext>()
            .UseNpgsql("Host=localhost;Database=eventstore;Username=postgres;Password=postgres")
            .Options;

        using var context = new PostgresContext(options);
        var entityType = context.Model.FindEntityType(typeof(DbEvent));
        var property = entityType?.FindProperty(nameof(DbEvent.Data));

        Assert.NotNull(property);
        Assert.Equal("jsonb", property!.GetColumnType());
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

        Assert.NotNull(property);
        Assert.Equal("nvarchar(max)", property!.GetColumnType());
    }

    
}
