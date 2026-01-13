using Testcontainers.MsSql;

namespace EventStoreCore.Tests;

public class SqlServerFixture : IAsyncLifetime
{

    protected readonly MsSqlContainer _sqlServerContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _sqlServerContainer.StartAsync();
    }
    public async ValueTask DisposeAsync()
    {
        await _sqlServerContainer.DisposeAsync();
    }

    internal string ConnectionString
    {
        get
        {
            var connectionString = _sqlServerContainer.GetConnectionString();
            // If not containing "Database=", add it with a default database name
            if (!connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase))
            {
                connectionString += ";Database=test";
            }
            else
            {
                // replace database value
                connectionString = connectionString.Replace("Database=master", "Database=test");
            }

            return connectionString;
        }
    }
}
