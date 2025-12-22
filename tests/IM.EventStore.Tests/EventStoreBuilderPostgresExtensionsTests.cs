using IM.EventStore;
using IM.EventStore.Abstractions;
using IM.EventStore.Persistence.EntityFrameworkCore.Postgres;
using IM.EventStore.Testing;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.Tests;

public class EventStoreBuilderPostgresExtensionsTests
{
    private sealed class FakeDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeoutValue timeout = default) => null;
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeoutValue timeout = default, CancellationToken cancellationToken = default) => new((IDistributedSynchronizationHandle?)null);
    }

    private sealed class FakeProjectionOptions : IProjectionOptions
    {
        public bool HandlesAllCalled { get; private set; }
        public void Handles<T>() where T : class => HandlesAllCalled = true;
        public void HandlesAll() => HandlesAllCalled = true;
        public void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = null) where TEvent : class => HandlesAllCalled = true;
    }

    private sealed class FakeRegistrar : IProjectionRegistrar, ISubscriptionDaemonRegistrar
    {
        public Func<IServiceProvider, IDistributedLockProvider>? AddedFactory { get; private set; }
        public ProjectionMode? AddedMode { get; private set; }
        public Action<IProjectionOptions>? AddedConfigure { get; private set; }

        public void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory)
        {
            AddedFactory = factory;
        }

        public void AddProjection<TProjection, TSnapshot>(ProjectionMode mode, Action<IProjectionOptions>? configure) where TProjection : IProjection<TSnapshot>, new() where TSnapshot : class, new()
        {
            AddedMode = mode;
            AddedConfigure = configure;
        }
    }

    [Fact]
    public void AddSubscriptionDaemon_ThrowsWhenProviderMissing()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddSubscriptionDaemon<TestDbContext>(_ => new FakeDistributedLockProvider()));

        Assert.Equal("No EF Core provider is registered. Call ExistingDbContext<TDbContext>() first.", exception.Message);
    }

    [Fact]
    public void AddProjection_ThrowsWhenProviderMissing()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddProjection<TestDbContext, DummyProjection, DummySnapshot>());

        Assert.Equal("No EF Core provider is registered. Call ExistingDbContext<TDbContext>() first.", exception.Message);
    }

    [Fact]
    public void AddSubscriptionDaemon_ForwardsToRegistrar()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var returned = builder.AddSubscriptionDaemon<TestDbContext>(_ => new FakeDistributedLockProvider());

        Assert.Same(builder, returned);
        Assert.NotNull(registrar.AddedFactory);
    }

    [Fact]
    public void AddProjection_ForwardsToRegistrar()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var returned = builder.AddProjection<TestDbContext, DummyProjection, DummySnapshot>(ProjectionMode.Eventual, options => options.HandlesAll());

        Assert.Same(builder, returned);
        Assert.Equal(ProjectionMode.Eventual, registrar.AddedMode);
        Assert.NotNull(registrar.AddedConfigure);

        var projectionOptions = new FakeProjectionOptions();
        registrar.AddedConfigure!(projectionOptions);
        Assert.True(projectionOptions.HandlesAllCalled);
    }

    private class DummyProjection : IProjection<DummySnapshot>
    {
        public Task Apply(IEvent @event, DummySnapshot state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class DummySnapshot;
}
