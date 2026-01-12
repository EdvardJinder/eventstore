using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using EventStoreCore.Testing;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventStoreCore.Tests;

public class EventStoreBuilderPostgresExtensionsTests
{
    private sealed class FakeProjectionOptions : IProjectionOptions
    {
        public bool HandlesAllCalled { get; private set; }
        public void Handles<T>() where T : class => HandlesAllCalled = true;
        public void HandlesAll() => HandlesAllCalled = true;
        public void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = null) where TEvent : class => HandlesAllCalled = true;
    }

    private sealed class FakeRegistrar : IProjectionRegistrar, ISubscriptionDaemonRegistrar, IProjectionDaemonRegistrar
    {
        public Func<IServiceProvider, IDistributedLockProvider>? AddedFactory { get; private set; }
        public ProjectionMode? AddedMode { get; private set; }
        public Action<IProjectionOptions>? AddedConfigure { get; private set; }
        public bool ProjectionDaemonAdded { get; private set; }

        public void AddSubscriptionDaemon(Func<IServiceProvider, IDistributedLockProvider> factory)
        {
            AddedFactory = factory;
        }

        public void AddProjection<TProjection, TSnapshot>(ProjectionMode mode, Action<IProjectionOptions>? configure) where TProjection : IProjection<TSnapshot>, new() where TSnapshot : class, new()
        {
            AddedMode = mode;
            AddedConfigure = configure;
        }

        public void AddProjectionDaemon(Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory, Action<ProjectionDaemonOptions>? configure = null)
        {
            AddedFactory = lockProviderFactory;
            ProjectionDaemonAdded = true;
        }
    }

    [Fact]
    public void AddSubscriptionDaemon_ThrowsWhenProviderMissing()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddSubscriptionDaemon<Tests.EventStoreFixture.EventStoreDbContext>(_ => Substitute.For<IDistributedLockProvider>()));

        Assert.Equal("No EF Core provider is registered. Call ExistingDbContext<TDbContext>() first.", exception.Message);
    }

    [Fact]
    public void AddProjection_ThrowsWhenProviderMissing()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddProjection<EventStoreFixture.EventStoreDbContext, DummyProjection, DummySnapshot>());

        Assert.Equal("No EF Core provider is registered. Call ExistingDbContext<TDbContext>() first.", exception.Message);
    }

    [Fact]
    public void AddSubscriptionDaemon_ForwardsToRegistrar()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var returned = builder.AddSubscriptionDaemon<EventStoreFixture.EventStoreDbContext>(_ => Substitute.For<IDistributedLockProvider>());

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

        var returned = builder.AddProjection<EventStoreFixture.EventStoreDbContext, DummyProjection, DummySnapshot>(ProjectionMode.Eventual, options => options.HandlesAll());

        Assert.Same(builder, returned);
        Assert.Equal(ProjectionMode.Eventual, registrar.AddedMode);
        Assert.NotNull(registrar.AddedConfigure);

        var projectionOptions = new FakeProjectionOptions();
        registrar.AddedConfigure!(projectionOptions);
        Assert.True(projectionOptions.HandlesAllCalled);
    }

    private class DummyProjection : IProjection<DummySnapshot>
    {
        public static Task Evolve(DummySnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct) => Task.CompletedTask;

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private class DummySnapshot
    {
    }
}
