using EventStoreCore;
using EventStoreCore.Abstractions;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EventStoreCore.Tests;

public sealed class DaemonOptionsTests
{
    [Fact]
    public void ProjectionDaemonOptions_HasExpectedSchedulingDefaults()
    {
        var options = new ProjectionDaemonOptions();

        Assert.Equal(8, options.MaxConcurrentWorkers);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.LockTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RetryDelay);
        Assert.Equal(CheckpointScope.Global, options.CheckpointScope);
    }

    [Fact]
    public void EntityOutboxOptions_HasExpectedSchedulingDefaults()
    {
        var options = new EntityOutboxOptions();

        Assert.Equal(8, options.MaxConcurrentWorkers);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(10), options.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.LockTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RetryDelay);
        Assert.Equal(CheckpointScope.Global, options.CheckpointScope);
    }

    [Fact]
    public void SubscriptionDaemon_RejectsNonPositiveWorkerLimit()
    {
        var services = new ServiceCollection();
        var builder = new EfCoreEventEventStoreBuilder<ValidationDbContext>(services);
        builder.AddSubscriptionDaemon(
            _ => Substitute.For<IDistributedLockProvider>(),
            options => options.MaxConcurrentWorkers = 0);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SubscriptionOptions>>().Value);
    }

    [Fact]
    public void ProjectionDaemon_RejectsNonPositiveWorkerLimit()
    {
        var services = new ServiceCollection();
        var builder = new EfCoreEventEventStoreBuilder<ValidationDbContext>(services);
        builder.AddProjectionDaemon(
            _ => Substitute.For<IDistributedLockProvider>(),
            options => options.MaxConcurrentWorkers = 0);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ProjectionDaemonOptions>>().Value);
    }

    [Fact]
    public void EntityOutboxDaemon_RejectsNonPositiveWorkerLimit()
    {
        var services = new ServiceCollection();
        services.AddEntityOutboxDaemon<ValidationDbContext>(
            _ => Substitute.For<IDistributedLockProvider>(),
            options => options.MaxConcurrentWorkers = 0);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EntityOutboxOptions>>().Value);
    }

    private sealed class ValidationDbContext(
        DbContextOptions<ValidationDbContext> options) : DbContext(options);
}
