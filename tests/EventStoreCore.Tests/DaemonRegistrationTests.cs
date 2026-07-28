using EventStoreCore.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public sealed class DaemonRegistrationTests
{
    private sealed class SampleEvent;

    private sealed class UntypedSubscription : ISubscription
    {
        public Task Handle(IEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TypedSubscription : ISubscription<SampleEvent>
    {
        public IEvent<SampleEvent>? LastEvent { get; private set; }

        public Task Handle(IEvent<SampleEvent> @event, CancellationToken ct)
        {
            LastEvent = @event;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void ExplicitSubscriptionNameIsUsedByRegistration()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddSubscription<UntypedSubscription>(options => options.Name = "billing");

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<SubscriptionRegistration>();

        Assert.Equal("billing", registration.Name);
    }

    [Fact]
    public void DuplicateSubscriptionNamesAreRejected()
    {
        var builder = new EventStoreBuilder(new ServiceCollection());
        builder.AddSubscription<UntypedSubscription>(options => options.Name = "billing");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddSubscription<TypedSubscription, SampleEvent>(options => options.Name = "billing"));

        Assert.Contains("billing", exception.Message);
    }

    [Fact]
    public void TypedRegistrationAddsClrFilter()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddSubscription<TypedSubscription, SampleEvent>();

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<SubscriptionRegistration>();

        Assert.True(registration.Options.MatchesMaterialized(typeof(SampleEvent)));
        Assert.False(registration.Options.MatchesMaterialized(typeof(object)));
    }

    [Fact]
    public void SubscriptionFiltersCombineCategoriesAndAllowMultipleValues()
    {
        var tenant = Guid.NewGuid();
        var stream = Guid.NewGuid();
        var options = new SubscriptionRegistrationOptions();
        options.IncludeLogicalEventType("order_created");
        options.IncludeLogicalEventType("order_updated");
        options.IncludeStreamType("orders");
        options.IncludeStream(stream);
        options.IncludeTenant(tenant);

        var matching = new DbEvent
        {
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            TypeName = "order_updated",
            StreamType = "orders",
            StreamId = stream,
            TenantId = tenant
        };

        Assert.True(options.MatchesPersisted(matching));
        Assert.False(options.MatchesPersisted(new DbEvent
        {
            Type = typeof(SampleEvent).AssemblyQualifiedName!,
            TypeName = "order_updated",
            StreamType = "other",
            StreamId = stream,
            TenantId = tenant
        }));
    }

    [Fact]
    public void HandleUnknownSelectsCustomPolicy()
    {
        var options = new SubscriptionRegistrationOptions();

        options.HandleUnknown((_, _) => ValueTask.CompletedTask);

        Assert.Equal(UnknownEventPolicy.Custom, options.UnknownEventPolicy);
        Assert.NotNull(options.UnknownEventHandler);
    }

    [Fact]
    public void ProjectionNameRejectsWhitespace()
    {
        var options = new ProjectionOptions();

        Assert.Throws<ArgumentException>(() => options.Name(" "));
    }

    [Fact]
    public void HealthMonitorUsesInjectedTimeProviderForStallDetection()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));
        var monitor = new DaemonHealthMonitor(time);
        monitor.Heartbeat("orders", "subscription");

        time.Advance(TimeSpan.FromMinutes(6));

        var report = monitor.CheckHealth(TimeSpan.FromMinutes(5));

        Assert.Equal(DaemonHealthStatus.Degraded, report.Status);
        Assert.Single(report.Entries);
        Assert.Equal(time.GetUtcNow().AddMinutes(-6), report.Entries[0].LastHeartbeat);
    }

    [Fact]
    public void Healthy_tenant_heartbeat_does_not_clear_another_tenant_fault()
    {
        var monitor = new DaemonHealthMonitor();
        var faultedTenant = CheckpointScopeKey.Tenant(Guid.NewGuid());
        var healthyTenant = CheckpointScopeKey.Tenant(Guid.NewGuid());

        monitor.Fault(
            "orders",
            "subscription",
            new InvalidOperationException("failed"),
            faultedTenant);
        monitor.Heartbeat("orders", "subscription", healthyTenant);

        var report = monitor.CheckHealth(TimeSpan.FromMinutes(5));

        Assert.Equal(DaemonHealthStatus.Unhealthy, report.Status);
        Assert.Equal(2, report.Entries.Count);
        Assert.Single(report.Entries, entry => entry.IsFaulted);
    }
}
