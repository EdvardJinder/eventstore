using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EJ.EventStore.Tests;

public class LockingTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{


    [Fact]
    public async Task should_aquire_lock_when_available()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            return new PostgresDistributedSynchronizationProvider(fixture.ConnectionString);
        });

        services.AddLogging();

        var provider = services.BuildServiceProvider();


        var locking = provider.GetRequiredService<IDistributedLockProvider>();

        using var aquired = await locking.TryAcquireLockAsync("lock", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(aquired != null, "Failed to acquire lock");
    }

    [Fact]
    public async Task should_not_aquire_lock_when_not_available()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            return new PostgresDistributedSynchronizationProvider(fixture.ConnectionString);
        });
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var locking = provider.GetRequiredService<IDistributedLockProvider>();

        using var aquired1 = await locking.TryAcquireLockAsync("lock", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(aquired1 != null, "Failed to acquire first lock");

        using var aquired2 = await locking.TryAcquireLockAsync("lock", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(aquired2 == null, "Acquired second lock when it should not be available");

    }
}
