using EventStoreCore.Testing;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.Tests;

public class TestDbContextCoverageTests
{
    [Fact]
    public void OnConfiguring_UsesInMemoryDatabase()
    {
        var dbContext = new TestDbContext(Guid.NewGuid().ToString("N"));

        var providerName = dbContext.Database.ProviderName;

        Assert.Contains("InMemory", providerName);
    }

    [Fact]
    public void DbSets_AreAvailable()
    {
        var dbContext = new TestDbContext(Guid.NewGuid().ToString("N"));

        Assert.NotNull(dbContext.Events);
        Assert.NotNull(dbContext.Stream);
    }
}
