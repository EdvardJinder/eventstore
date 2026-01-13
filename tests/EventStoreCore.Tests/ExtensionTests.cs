using EventStoreCore;

using EventStoreCore.Postgres;

using Microsoft.EntityFrameworkCore;

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
}
