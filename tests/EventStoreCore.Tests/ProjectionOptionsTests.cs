using EventStoreCore;

namespace EventStoreCore.Tests;

public class ProjectionOptionsTests
{
    private sealed class EventA;
    private sealed class EventB;
    private sealed class EventC;

    [Fact]
    public void HandlesAll_IsDefault()
    {
        var options = new ProjectionOptions();

        Assert.True(options.IsHandled(typeof(EventA)));
        Assert.True(options.IsHandled(typeof(EventB)));
    }

    [Fact]
    public void Handles_SwitchesToWhitelist()
    {
        var options = new ProjectionOptions();
        options.Handles<EventA>();

        Assert.True(options.IsHandled(typeof(EventA)));
        Assert.False(options.IsHandled(typeof(EventB)));
    }

    [Fact]
    public void Ignores_ExcludesTypeInHandlesAllMode()
    {
        var options = new ProjectionOptions();
        options.Ignores<EventA>();

        Assert.False(options.IsHandled(typeof(EventA)));
        Assert.True(options.IsHandled(typeof(EventB)));
    }

    [Fact]
    public void Ignores_ExcludesTypeInHandlesMode()
    {
        var options = new ProjectionOptions();
        options.Handles<EventA>();
        options.Handles<EventB>();
        options.Ignores<EventB>();

        Assert.True(options.IsHandled(typeof(EventA)));
        Assert.False(options.IsHandled(typeof(EventB)));
        Assert.False(options.IsHandled(typeof(EventC)));
    }

    [Fact]
    public void ShouldIgnoreUnknown_FalseByDefault()
    {
        var options = new ProjectionOptions();

        Assert.False(options.ShouldIgnoreUnknown);
    }

    [Fact]
    public void ShouldIgnoreUnknown_TrueWhenHandlesUsed()
    {
        var options = new ProjectionOptions();
        options.Handles<EventA>();

        Assert.True(options.ShouldIgnoreUnknown);
    }

    [Fact]
    public void ShouldIgnoreUnknown_TrueWhenIgnoreUnknownCalled()
    {
        var options = new ProjectionOptions();
        options.IgnoreUnknown();

        Assert.True(options.ShouldIgnoreUnknown);
    }

    [Fact]
    public void ShouldIgnoreUnknown_TrueWhenHandlesAllAndIgnoreUnknown()
    {
        var options = new ProjectionOptions();
        options.HandlesAll();
        options.IgnoreUnknown();

        Assert.True(options.ShouldIgnoreUnknown);
    }
}
