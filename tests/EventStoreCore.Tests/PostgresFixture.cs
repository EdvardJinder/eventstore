using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Testcontainers.PostgreSql;

namespace EventStoreCore.Tests;
public class PostgresFixture : IAsyncLifetime
{

    protected readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("eventstore")
        .WithUsername("postgres")
        .WithPassword("p@ssword!")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
    }
    public async ValueTask DisposeAsync()
    {
        await _postgreSqlContainer.DisposeAsync();
    }

    internal string ConnectionString => _postgreSqlContainer.GetConnectionString();
}
