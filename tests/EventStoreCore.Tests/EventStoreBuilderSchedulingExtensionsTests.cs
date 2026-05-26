using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;

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

        var returned = builder.AddScheduler(scheduler =>
        {
            scheduler.Services.AddSchedulerProvider("Hangfire");
            scheduler.Schedule<DummyEvent, DummyArgs>(
                e => ScheduleKey.Create($"dummy:{e.Data.Id}"),
                _ => TimeSpan.FromMinutes(1),
                e => new DummyArgs(e.Data.Id, e.Id));
        });

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

        builder.AddScheduler(scheduler => scheduler.Services.AddSchedulerProvider("Hangfire"));
        var returned = builder.AddScheduler(scheduler =>
        {
            scheduler.Services.AddSchedulerProvider("Hangfire");
            scheduler.Cancel<DummyEvent>(e => ScheduleKey.Create($"dummy:{e.Data.Id}"));
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
    public void ScheduleKey_Create_ThrowsWhenValueIsBlank()
    {
        var exception = Assert.Throws<ArgumentException>(() => ScheduleKey.Create(" "));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void AddScheduler_RegistersCommonScheduleMappings()
    {
        var services = new ServiceCollection();
        var builder = new EventStoreBuilder(services);

        builder.AddScheduler(scheduler =>
        {
            scheduler.Services.AddSchedulerProvider("Hangfire");
            scheduler.Schedule<DummyEvent, DummyArgs>(
                e => ScheduleKey.Create($"dummy:{e.Data.Id}"),
                _ => TimeSpan.FromMinutes(1),
                e => new DummyArgs(e.Data.Id, e.Id));
            scheduler.Cancel<DummyEvent>(e => ScheduleKey.Create($"dummy:{e.Data.Id}"));
        });

        var optionsFactory = services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<SchedulerOptions>>();
        Assert.Equal(2, optionsFactory.Value.Registrations.Count);
    }

    private sealed class DummyEvent
    {
        public Guid Id { get; init; }
    }

    private sealed record DummyArgs(Guid AggregateId, Guid SourceEventId);
}
