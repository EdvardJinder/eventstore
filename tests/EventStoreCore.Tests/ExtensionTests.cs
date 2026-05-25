using EventStoreCore;

using EventStoreCore.Postgres;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public class ExtensionTests
{
    [Fact]
    public void ResolvesEventStore()
    {
        var options = new DbContextOptionsBuilder<DbContext>()
            .Options;
        using var context = new DbContext(options);
        var eventStore = context.Streams;
        Assert.NotNull(eventStore);
        Assert.IsType<DbContextEventStore>(eventStore);
    }

    [Fact]
    public void ThrowsIfDbContextIsNull()
    {
        DbContext? context = null;
        Assert.Throws<ArgumentNullException>(() => context!.Streams);
    }

    [Fact]
    public void UseSnapshotsRejectsInvalidInterval()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddEventStore(builder =>
            {
                builder.UseSnapshots(snapshots =>
                {
                    snapshots.For<TestState>("orders", options => options.Interval = 0);
                });
            }));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void UseSnapshotsRejectsDuplicateStateForSameStreamType()
    {
        var services = new ServiceCollection();
        services.AddEventStore(builder =>
        {
            builder.UseSnapshots(snapshots =>
            {
                snapshots.For<TestState>("orders");
                snapshots.For<TestState>("orders");
            });
        });

        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SnapshotRegistry>());
    }

    private sealed class TestState : EventStoreCore.Abstractions.IState
    {
        public void Apply(EventStoreCore.Abstractions.IEvent @event)
        {
        }
    }
}
