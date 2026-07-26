using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Testing;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;


namespace EventStoreCore.Tests;

public class EventStoreBuilderPostgresExtensionsTests
{
    private sealed class FakeProjectionOptions : IProjectionOptions
    {
        public bool HandlesAllCalled { get; private set; }
        public void Name(string name) { }
        public void IncludeLogicalEventType(string logicalEventType) { }
        public void IncludeStreamType(string streamType) { }
        public void IncludeStream(Guid streamId) { }
        public void IncludeTenant(Guid tenantId) { }
        public void Handles<T>() where T : class => HandlesAllCalled = true;
        public void HandlesAll() => HandlesAllCalled = true;
        public void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = null) where TEvent : class => HandlesAllCalled = true;
        public void Ignores<T>() where T : class { }
        public void IgnoreUnknown() { }
    }

    private sealed class FakeRegistrar :
        IEfCoreEventStoreBuilder<EventStoreFixture.EventStoreDbContext>,
        IProjectionRegistrar,
        ISubscriptionDaemonRegistrar,
        IProjectionDaemonRegistrar
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public Func<IServiceProvider, IDistributedLockProvider>? AddedFactory { get; private set; }
        public ProjectionMode? AddedMode { get; private set; }
        public Action<IProjectionOptions>? AddedConfigure { get; private set; }
        public Action<SubscriptionOptions>? AddedSubscriptionConfigure { get; private set; }
        public bool ProjectionDaemonAdded { get; private set; }
        public Action<ProjectionDaemonOptions>? AddedDaemonConfigure { get; private set; }

        public void AddSubscriptionDaemon(
            Func<IServiceProvider, IDistributedLockProvider> factory,
            Action<SubscriptionOptions>? configure = null)
        {
            AddedFactory = factory;
            AddedSubscriptionConfigure = configure;
        }

        public void AddProjection<TProjection, TSnapshot>(ProjectionMode mode, Action<IProjectionOptions>? configure) where TProjection : IProjection<TSnapshot>, new() where TSnapshot : class, new()
        {
            AddedMode = mode;
            AddedConfigure = configure;
        }

        public void AddProjectionDaemon(Func<IServiceProvider, IDistributedLockProvider> lockProviderFactory, Action<ProjectionDaemonOptions>? configure = null)
        {
            AddedFactory = lockProviderFactory;
            AddedDaemonConfigure = configure;
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

    [Fact]
    public void AddSubscriptionDaemon_UsesDefaultLockProviderFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLockProvider>());
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var returned = builder.AddSubscriptionDaemon<EventStoreFixture.EventStoreDbContext>();

        Assert.Same(builder, returned);
        Assert.NotNull(registrar.AddedFactory);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddSubscriptionDaemon_PassesConfigureCallback()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLockProvider>());
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var configureCalled = false;
        var returned = builder.AddSubscriptionDaemon<EventStoreFixture.EventStoreDbContext>(options =>
        {
            configureCalled = true;
            options.BatchSize = 123;
            options.CheckpointFrequency = 7;
        });

        Assert.Same(builder, returned);
        Assert.NotNull(registrar.AddedFactory);
        Assert.NotNull(registrar.AddedSubscriptionConfigure);

        var options = new SubscriptionOptions();
        registrar.AddedSubscriptionConfigure!(options);

        Assert.True(configureCalled);
        Assert.Equal(123, options.BatchSize);
        Assert.Equal(7, options.CheckpointFrequency);
    }

    [Fact]
    public void AddProjectionDaemon_UsesDefaultLockProviderFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLockProvider>());
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var returned = builder.AddProjectionDaemon<EventStoreFixture.EventStoreDbContext>();

        Assert.Same(builder, returned);
        Assert.NotNull(registrar.AddedFactory);
        Assert.True(registrar.ProjectionDaemonAdded);
    }

    [Fact]
    public void AddProjectionDaemon_PassesConfigureCallback()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLockProvider>());
        var builder = new EventStoreBuilder(services);
        var registrar = new FakeRegistrar();
        builder.UseProvider(registrar);

        var configureCalled = false;
        var returned = builder.AddProjectionDaemon<EventStoreFixture.EventStoreDbContext>(options =>
        {
            configureCalled = true;
            options.BatchSize = 123;
        });

        Assert.Same(builder, returned);
        Assert.NotNull(registrar.AddedFactory);
        Assert.True(registrar.ProjectionDaemonAdded);
        Assert.NotNull(registrar.AddedDaemonConfigure);

        var options = new ProjectionDaemonOptions();
        registrar.AddedDaemonConfigure!(options);

        Assert.True(configureCalled);
        Assert.Equal(123, options.BatchSize);
    }

    [Fact]
    public void BuilderExtensions_RejectMismatchedDbContextType()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);
        builder.ExistingDbContext<ContextA>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddProjection<ContextB, DummyProjection, DummySnapshot>());

        Assert.Contains(typeof(ContextA).FullName!, exception.Message);
        Assert.Contains(typeof(ContextB).FullName!, exception.Message);
    }

    [Fact]
    public void ExistingDbContext_RejectsRepeatedProviderConfiguration()
    {
        var builder = new EventStoreBuilder(new ServiceCollection());
        builder.ExistingDbContext<ContextA>();

        Assert.Throws<InvalidOperationException>(() => builder.ExistingDbContext<ContextA>());
    }


    private class DummyProjection : IProjection<DummySnapshot>
    {
        public static Task Evolve(DummySnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct) => Task.CompletedTask;

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private class DummySnapshot
    {
    }

    private sealed class ContextA(DbContextOptions<ContextA> options) : DbContext(options);
    private sealed class ContextB(DbContextOptions<ContextB> options) : DbContext(options);
}
