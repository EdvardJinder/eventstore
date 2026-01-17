using EventStoreCore;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public class ProjectionContextExtensionsTests
{
    private sealed class TestContext : IProjectionContext
    {
        public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();
        public object? ProviderState { get; set; }
    }

    private sealed class DummyDbContext : DbContext
    {
        public DummyDbContext(DbContextOptions<DummyDbContext> options) : base(options)
        {
        }
    }

    [Fact]
    public void DbContext_ReturnsProviderState_WhenDbContextIsPresent()
    {
        var context = new TestContext();
        var dbContext = new DummyDbContext(new DbContextOptionsBuilder<DummyDbContext>().Options);
        context.ProviderState = dbContext;

        Assert.Same(dbContext, context.DbContext);
    }

    [Fact]
    public void DbContext_Throws_WhenProviderStateMissing()
    {
        var context = new TestContext { ProviderState = null };

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.DbContext);

        Assert.Contains("not created with an Entity Framework Core provider", exception.Message);
    }
}
