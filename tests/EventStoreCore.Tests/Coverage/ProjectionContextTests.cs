using EventStoreCore;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public class ProjectionContextTests
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
    public void ProviderState_CanBeRetrieved()
    {
        var context = new TestContext();
        var dbContext = new DummyDbContext(new DbContextOptionsBuilder<DummyDbContext>().Options);
        context.ProviderState = dbContext;

        Assert.Same(dbContext, context.ProviderState);
    }
}
