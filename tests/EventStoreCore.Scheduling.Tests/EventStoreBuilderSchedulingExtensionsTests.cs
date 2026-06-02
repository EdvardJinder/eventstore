using EventStoreCore.Hangfire;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Tests;

public class EventStoreBuilderSchedulingExtensionsTests
{
    [Fact]
    public void AddScheduler_ThrowsWhenProviderMissing()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddScheduler(_ => { }));

        Assert.Equal("No scheduler provider is registered. Call UsingX(...) before or inside AddScheduler(...).", exception.Message);
    }

    [Fact]
    public void AddScheduler_RegistersSelectedProvider()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var returned = builder.AddScheduler(scheduler => scheduler.UsingHangfire());

        Assert.Same(builder, returned);

        var registration = services
            .Single(d => d.ServiceType == typeof(SchedulerProviderRegistration))
            .ImplementationInstance as SchedulerProviderRegistration;

        Assert.NotNull(registration);
        Assert.Equal("Hangfire", registration.ProviderName);
    }

    [Fact]
    public void AddScheduler_ThrowsWhenProviderRegisteredTwiceInSameCall()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddScheduler(scheduler =>
            {
                scheduler.Services.AddSchedulerProvider("Hangfire");
                scheduler.Services.AddSchedulerProvider("Quartz");
            }));

        Assert.Equal(
            "A scheduler provider is already registered for this service collection ('Hangfire'). Only one scheduler provider can be configured.",
            exception.Message);
    }

    [Fact]
    public void AddScheduler_AllowsAdditionalMappingsAcrossCallsWhenProviderMatches()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddScheduler(scheduler => scheduler.UsingHangfire());
        var returned = builder.AddScheduler(scheduler =>
        {
            scheduler.UsingHangfire();
            scheduler.On<DummyEvent>().Hangfire(static (_, _, _, _) => ValueTask.CompletedTask);
        });

        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddScheduler_ThrowsWhenDifferentProviderRegisteredAcrossCalls()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddScheduler(scheduler => scheduler.Services.AddSchedulerProvider("Hangfire"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddScheduler(scheduler => scheduler.Services.AddSchedulerProvider("Quartz")));

        Assert.Equal(
            "A scheduler provider is already registered for this service collection ('Hangfire'). Only one scheduler provider can be configured.",
            exception.Message);
    }

    [Fact]
    public void AddScheduler_RegistersProviderNativeActions()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddScheduler(scheduler =>
        {
            scheduler.UsingHangfire();
            scheduler.On<DummyEvent>().Hangfire(static (_, _, _, _) => ValueTask.CompletedTask);
            scheduler.On<DummyEvent>().Hangfire("second-dummy-action", static (_, _, _, _) => ValueTask.CompletedTask);
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SchedulerOptions>>().Value;

        Assert.Equal(2, options.Registrations.Count);
        Assert.Contains(options.Registrations, r => r.RegistrationName.EndsWith("DummyEvent", StringComparison.Ordinal));
        Assert.Contains(options.Registrations, r => r.RegistrationName == "second-dummy-action");
    }

    [Fact]
    public void AddScheduler_ThrowsWhenSameProviderEventActionNameIsRegisteredTwice()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddScheduler(scheduler =>
        {
            scheduler.UsingHangfire();
            scheduler.On<DummyEvent>().Hangfire(static (_, _, _, _) => ValueTask.CompletedTask);
            scheduler.On<DummyEvent>().Hangfire(static (_, _, _, _) => ValueTask.CompletedTask);
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IOptions<SchedulerOptions>>().Value);

        Assert.Contains("Use an explicit unique name for each action.", exception.Message, StringComparison.Ordinal);
    }

    private sealed class DummyEvent
    {
        public Guid Id { get; init; }
    }
}
